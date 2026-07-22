using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

// SyntheticMind — the mind itself, not a pipeline you invoke (ARCHITECTURE §5). One executable, one
// continuous heartbeat: it watches the way a child does — attends to the PERSON and glances at what
// they hold up (person-centred attention, 039); it hears WORDS, cut from continuous speech at the
// pauses (037); it binds a word to the object it was attending to; it remembers what each word sounds
// like over time; and when it recognises an object it has a word for, it SAYS that word, as a syllable
// trajectory, with a voice it taught itself by babbling (040/041). Then again. Forever, until Ctrl-C.
//
//   dotnet run --project src/SyntheticMind -- [folder-of-videos]
//
// Watches a folder of videos on an endless loop (its "world"), a little better each pass. Everything
// unsupervised, local, online — no labels, no backprop. The whole project, running as one process.

const int SampleRate = 16000, Hop = 160, Fft = 512, MelBands = 20, CamW = 120, CamH = 90, FrameStride = 3;
const int FoveaGrid = 8, Orientations = 4;
const int TrajKeys = 3, TrajFrames = 8, SaySamples = 3600;

var world = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "temp");
var exts = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".m4v" };
var videos = Directory.Exists(world)
    ? Directory.GetFiles(world).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray()
    : [];
if (videos.Length == 0) { Console.WriteLine($"\n  no world to perceive in {world}. Pass a folder of videos.\n"); return; }

var stateDir = Path.GetFullPath("mind-state");
var saidDir = Path.Combine(stateDir, "said");
Directory.CreateDirectory(saidDir);
var objectCbPath = Path.Combine(stateDir, "object-codebook.json");
var wordCbPath = Path.Combine(stateDir, "word-codebook.json");
var pairingsPath = Path.Combine(stateDir, "pairings.json");

// --- the mind's persistent faculties ----------------------------------------------------------
var cochlea = new Cochlea(SampleRate, Fft, MelBands);
var audioL0 = new Unit(new LearnedPredictiveRule(MelBands, stateWidth: 8, history: 8, quadraticFeatures: 0));
var attention = new ObjectAttention(new Retina(FoveaGrid, motion: false, orientations: Orientations, color: true), mode: AttentionMode.PersonCentred);
var objectWidth = attention.Width;

var wordVq = VectorQuantizer.LoadOrNew(wordCbPath, capacity: 48, newUnitThreshold: 0.20f);
var objectVq = VectorQuantizer.LoadOrNew(objectCbPath, capacity: 64, newUnitThreshold: 0.30f, subtractRunningMean: true);
if (objectVq.Count > 0 && objectVq.Dimension != objectWidth)   // eye changed since last life → start fresh
{ wordVq = new VectorQuantizer(48, 0.20f); objectVq = new VectorQuantizer(64, 0.30f, subtractRunningMean: true); }
var binder = CrossSituationalBinder.LoadOrNew(pairingsPath);   // word (heard) ↔ object (seen)

// The mouth speaks in TRAJECTORIES, and the mind remembers what each word sounds like over time.
var synth = new FormantSynth(SampleRate);
float[][] ToKeys(float[] c)
{
    var keys = new float[TrajKeys][];
    for (var k = 0; k < TrajKeys; k++) { keys[k] = new float[synth.ControlCount]; Array.Copy(c, k * synth.ControlCount, keys[k], 0, synth.ControlCount); }
    return keys;
}
float[] MelTrajectory(float[] wave)
{
    var traj = new float[TrajFrames * MelBands];
    for (var f = 0; f < TrajFrames; f++)
    {
        var s = Math.Clamp((int)((f + 0.5f) / TrajFrames * wave.Length) - Fft / 2, 0, Math.Max(0, wave.Length - Fft));
        var frame = new float[Fft];
        Array.Copy(wave, s, frame, 0, Math.Min(Fft, wave.Length - s));
        var mel = cochlea.Process(frame);
        Array.Copy(mel, 0, traj, f * MelBands, MelBands);
    }
    return traj;
}
var mouth = new VocalBabbler(TrajKeys * synth.ControlCount, TrajFrames * MelBands,
    c => MelTrajectory(synth.SynthesizeTrajectory(ToKeys(c), SaySamples)), seed: 1, normalizeMel: true);
var soundMemory = new Dictionary<int, float[]>();   // word-unit → the mel-trajectory it last heard for it
Console.WriteLine("\n  SyntheticMind — waking up. babbling to learn its own voice...");
for (var i = 0; i < 600; i++) mouth.Babble();

// --- graceful shutdown: on Ctrl-C, finish the thought and remember ----------------------------
var alive = true;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; alive = false; Console.WriteLine("\n  ...going to sleep. remembering."); };
void Remember() { objectVq.Save(objectCbPath); wordVq.Save(wordCbPath); binder.Save(pairingsPath); }

Console.WriteLine($"  awake. watching {videos.Length} video(s) in {world} on a loop. Ctrl-C to sleep.");
if (binder.Episodes > 0) Console.WriteLine($"  (remembering a past life: {wordVq.Count} words, {objectVq.Count} objects, {binder.Episodes} bindings)");
Console.WriteLine();

// --- the heartbeat: attend to an object → hear words → bind → (recognise → say the word) --------
float audioBaseline = 1e-4f;
long tick = 0, spoken = 0, wordsHeard = 0;
var lastSpeakTick = -10000L;
var pass = 0;

while (alive)
{
    pass++;
    foreach (var video in videos)
    {
        if (!alive) break;
        var samples = ExtractAudio(video, SampleRate);
        using var cap = new VideoCapture(video);
        if (!cap.IsOpened()) continue;

        var seg = new WordSegmenter(MelBands);      // a fresh ear-for-words per clip
        using var frame = new Mat();
        using var small = new Mat();
        var luma = new float[CamW * CamH];
        var red = new float[CamW * CamH];
        var green = new float[CamW * CamH];
        var blue = new float[CamW * CamH];
        var bgr = new byte[CamW * CamH * 3];
        var buf = new float[Fft];
        var samplePos = 0; var hopsDone = 0; var frameIndex = 0; var currentObject = -1;

        while (alive)
        {
            var process = frameIndex % FrameStride == 0;
            var ok = process ? cap.Read(frame) : cap.Grab();
            if (!ok || (process && frame.Empty())) break;
            frameIndex++;
            if (!process) continue;

            var tMs = cap.Get(VideoCaptureProperties.PosMsec);

            // See: attend to an object (the person, or what they hold up) → object-unit.
            Cv2.Resize(frame, small, new Size(CamW, CamH));
            Marshal.Copy(small.Data, bgr, 0, bgr.Length);
            for (var i = 0; i < CamW * CamH; i++)
            {
                float b = bgr[i * 3] / 255f, g = bgr[i * 3 + 1] / 255f, r = bgr[i * 3 + 2] / 255f;
                blue[i] = b; green[i] = g; red[i] = r;
                luma[i] = 0.114f * b + 0.587f * g + 0.299f * r;
            }
            var (objFeat, _, _, _, _) = attention.Attend(luma, red, green, blue, CamW, CamH);
            currentObject = objectVq.Quantize(objFeat);

            // Hear: catch the ear up to now, learn to predict sound, and segment WORDS at the pauses.
            var targetHop = (int)(tMs / 1000.0 * SampleRate / Hop);
            while (hopsDone < targetHop && samplePos + Fft <= samples.Length)
            {
                Array.Copy(buf, Hop, buf, 0, Fft - Hop);
                var energy = 0f;
                for (var i = Fft - Hop; i < Fft; i++) { buf[i] = samples[samplePos++]; energy += buf[i] * buf[i]; }
                energy /= Hop;
                var mel = cochlea.Process(buf);
                var s = audioL0.Observe(mel).SquaredError;
                audioBaseline += 0.001f * (s - audioBaseline);
                var word = seg.Accept(mel, energy);
                hopsDone++;
                if (word is null) continue;

                // A word just ended: bind it to whatever object it's attending to, and remember its sound.
                var wu = wordVq.Quantize(word);
                if (currentObject >= 0) binder.Observe([wu], [currentObject]);
                var wStart = Math.Max(0, samplePos - SaySamples);
                if (samplePos - wStart >= Fft) soundMemory[wu] = MelTrajectory(samples[wStart..samplePos]);
                wordsHeard++;
            }

            // Recognise & say: does the object it's attending to have a word it remembers? Say it.
            if (currentObject >= 0 && tick - lastSpeakTick > 300)
            {
                var recalled = binder.HeardForSeen(currentObject, minJointCount: 4);
                if (recalled is { } r && soundMemory.TryGetValue(r.Heard, out var target))
                {
                    var (control, _, _) = mouth.Imitate(target, refineSteps: 80);
                    WavWriter.WriteMono(Path.Combine(saidDir, $"utt{spoken:D4}.wav"), synth.SynthesizeTrajectory(ToKeys(control), SaySamples), SampleRate);
                    Console.WriteLine($"  [tick {tick,7}] sees object #{currentObject} — remembers its word (#{r.Heard}, bound {r.JointCount}x) — says it → said/utt{spoken:D4}.wav");
                    lastSpeakTick = tick; spoken++;
                }
            }

            tick++;
            if (tick % 2000 == 0)
            {
                Console.WriteLine($"  [tick {tick,7}] pass {pass} · {wordVq.Count} words / {objectVq.Count} objects / {binder.Episodes} bindings · heard {wordsHeard} words · ear-surprise {audioBaseline:F3} · said {spoken}x");
                Remember();
            }
        }
    }
    Console.WriteLine($"  [tick {tick,7}] — finished a full pass over its world; going round again.");
}

Remember();
Console.WriteLine($"  asleep. lived {tick} moments, learned {wordVq.Count} words + {objectVq.Count} objects, said {spoken}x. memory in {stateDir}\\\n");

static float[] ExtractAudio(string video, int rate)
{
    var tmp = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}.wav");
    try
    {
        var psi = new ProcessStartInfo("ffmpeg", $"-y -v error -i \"{video}\" -vn -ac 1 -ar {rate} \"{tmp}\"")
        { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi); if (p is null) return [];
        p.WaitForExit();
        return File.Exists(tmp) ? WavReader.ReadMono(tmp, rate) : [];
    }
    catch { return []; }
    finally { if (File.Exists(tmp)) File.Delete(tmp); }
}
