using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

// SyntheticMind — the mind itself, not a pipeline you invoke (ARCHITECTURE §5). One executable, one
// continuous heartbeat. It watches the way a child does: attends to the person and glances at what
// they hold up (person-centred attention, 039); hears WORDS cut from speech at the pauses (037);
// binds a word to the object it was attending to; remembers what each word sounds like over time;
// and when it recognises an object it has a word for, SAYS that word as a syllable trajectory, with a
// voice it taught itself by babbling (040/041). Then again. Forever, until Ctrl-C.
//
//   dotnet run --project src/SyntheticMind -- [folder-of-videos]   # watch a folder on a loop
//   dotnet run --project src/SyntheticMind -- --live               # perceive the real room (webcam + mic)
//
// Unsupervised, local, online — no labels, no backprop. The whole project, as one living process.

const int SampleRate = 16000, Hop = 160, Fft = 512, MelBands = 20, CamW = 120, CamH = 90, FrameStride = 3;
const int FoveaGrid = 8, Orientations = 4;
const int TrajKeys = 3, TrajFrames = 8, SaySamples = 3600;

var live = args.Contains("--live");
var world = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "temp");

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
if (objectVq.Count > 0 && objectVq.Dimension != objectWidth)
{ wordVq = new VectorQuantizer(48, 0.20f); objectVq = new VectorQuantizer(64, 0.30f, subtractRunningMean: true); }
var binder = CrossSituationalBinder.LoadOrNew(pairingsPath);   // word (heard) ↔ object (seen)

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
var soundMemory = new Dictionary<int, float[]>();
Console.WriteLine("\n  SyntheticMind — waking up. babbling to learn its own voice...");
for (var i = 0; i < 600; i++) mouth.Babble();

// --- shared perception (same brain, whether the world is a video file or the live room) --------
var buf = new float[Fft];
var hopBuf = new float[Hop];
var hopFill = 0;
var ring = new float[SaySamples]; var ringPos = 0; var ringCount = 0;   // recent raw audio, for word capture
float audioBaseline = 1e-4f;
var currentObject = -1;
var seg = new WordSegmenter(MelBands);
long tick = 0, spoken = 0, wordsHeard = 0;
var lastSpeakTick = -10000L;

var luma = new float[CamW * CamH];
var red = new float[CamW * CamH];
var green = new float[CamW * CamH];
var blue = new float[CamW * CamH];
var bgr = new byte[CamW * CamH * 3];
var small = new Mat();

void SeeFrame(Mat frame)
{
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
}

float[] Recent()
{
    var outv = new float[ringCount];
    for (var i = 0; i < ringCount; i++) outv[i] = ring[(ringPos - ringCount + i + SaySamples) % SaySamples];
    return outv;
}

void HearBlock(ReadOnlySpan<float> block)
{
    foreach (var sample in block)
    {
        ring[ringPos] = sample; ringPos = (ringPos + 1) % SaySamples; if (ringCount < SaySamples) ringCount++;
        hopBuf[hopFill++] = sample;
        if (hopFill < Hop) continue;

        Array.Copy(buf, Hop, buf, 0, Fft - Hop);
        Array.Copy(hopBuf, 0, buf, Fft - Hop, Hop);
        var energy = 0f;
        for (var i = 0; i < Hop; i++) energy += hopBuf[i] * hopBuf[i];
        energy /= Hop;
        hopFill = 0;

        var mel = cochlea.Process(buf);
        var s = audioL0.Observe(mel).SquaredError;
        audioBaseline += 0.001f * (s - audioBaseline);
        var word = seg.Accept(mel, energy);
        if (word is null) continue;

        var wu = wordVq.Quantize(word);                       // a word just ended
        if (currentObject >= 0) binder.Observe([wu], [currentObject]);   // bind it to the attended object
        if (ringCount >= Fft) soundMemory[wu] = MelTrajectory(Recent());  // remember what it sounds like
        wordsHeard++;
    }
}

void MaybeSpeak()
{
    if (currentObject < 0 || tick - lastSpeakTick <= 300) return;
    var recalled = binder.HeardForSeen(currentObject, minJointCount: 4);
    if (recalled is not { } r || !soundMemory.TryGetValue(r.Heard, out var target)) return;
    var (control, _, _) = mouth.Imitate(target, refineSteps: 80);
    WavWriter.WriteMono(Path.Combine(saidDir, $"utt{spoken:D4}.wav"), synth.SynthesizeTrajectory(ToKeys(control), SaySamples), SampleRate);
    Console.WriteLine($"  [tick {tick,7}] sees object #{currentObject} — remembers its word (#{r.Heard}, bound {r.JointCount}x) — says it → said/utt{spoken:D4}.wav");
    lastSpeakTick = tick; spoken++;
}

// --- graceful shutdown & memory ---------------------------------------------------------------
var alive = true;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; alive = false; Console.WriteLine("\n  ...going to sleep. remembering."); };
void Remember() { objectVq.Save(objectCbPath); wordVq.Save(wordCbPath); binder.Save(pairingsPath); }
void Status(string where) => Console.WriteLine($"  [tick {tick,7}] {where} · {wordVq.Count} words / {objectVq.Count} objects / {binder.Episodes} bindings · heard {wordsHeard} words · ear-surprise {audioBaseline:F3} · said {spoken}x");

if (binder.Episodes > 0) Console.WriteLine($"  (remembering a past life: {wordVq.Count} words, {objectVq.Count} objects, {binder.Episodes} bindings)");

if (live) RunLive(); else RunWorld();

Remember();
Console.WriteLine($"\n  asleep. lived {tick} moments, learned {wordVq.Count} words + {objectVq.Count} objects, said {spoken}x. memory in {stateDir}\\\n");
return;

// --- driver: the live room (webcam + mic) -----------------------------------------------------
void RunLive()
{
    VideoCapture? cam = null;
    try { cam = new VideoCapture(0); } catch { }
    if (cam is null || !cam.IsOpened()) { Console.WriteLine("  no webcam on device 0 — can't perceive the room.\n"); alive = false; return; }

    var micQueue = new Queue<float>();
    var micGate = new object();
    WaveInEvent? mic = null;
    try
    {
        mic = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 20 };
        mic.DataAvailable += (_, e) => { lock (micGate) for (var i = 0; i + 1 < e.BytesRecorded; i += 2) micQueue.Enqueue(BitConverter.ToInt16(e.Buffer, i) / 32768f); };
        mic.StartRecording();
    }
    catch (Exception ex) { Console.WriteLine($"  (no microphone: {ex.Message} — it will see but not hear words)"); }

    Console.WriteLine("  awake. perceiving the real room (webcam + mic). Ctrl-C to sleep.\n");
    using var frame = new Mat();
    while (alive)
    {
        if (!cam.Read(frame) || frame.Empty()) { Thread.Sleep(5); continue; }
        SeeFrame(frame);

        float[] block;
        lock (micGate) { block = micQueue.ToArray(); micQueue.Clear(); }
        if (block.Length > 0) HearBlock(block);

        MaybeSpeak();
        tick++;
        if (tick % 500 == 0) { Status("live"); Remember(); }
    }
    mic?.StopRecording(); mic?.Dispose(); cam.Dispose();
}

// --- driver: a folder of videos, watched on an endless loop ------------------------------------
void RunWorld()
{
    var exts = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".m4v" };
    var videos = Directory.Exists(world)
        ? Directory.GetFiles(world).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray()
        : [];
    if (videos.Length == 0) { Console.WriteLine($"  no world to perceive in {world}. Pass a folder of videos, or --live.\n"); alive = false; return; }

    Console.WriteLine($"  awake. watching {videos.Length} video(s) in {world} on a loop. Ctrl-C to sleep.\n");
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
            seg = new WordSegmenter(MelBands);   // a fresh ear-for-words per clip
            hopFill = 0; ringCount = 0; ringPos = 0;
            using var frame = new Mat();
            var samplePos = 0; var frameIndex = 0;

            while (alive)
            {
                var doFrame = frameIndex % FrameStride == 0;
                var ok = doFrame ? cap.Read(frame) : cap.Grab();
                if (!ok || (doFrame && frame.Empty())) break;
                frameIndex++;
                if (!doFrame) continue;

                SeeFrame(frame);
                var target = Math.Min(samples.Length, (int)(cap.Get(VideoCaptureProperties.PosMsec) / 1000.0 * SampleRate));
                if (target > samplePos) { HearBlock(samples.AsSpan(samplePos, target - samplePos)); samplePos = target; }

                MaybeSpeak();
                tick++;
                if (tick % 2000 == 0) { Status($"pass {pass}"); Remember(); }
            }
        }
        Console.WriteLine($"  [tick {tick,7}] — finished a full pass over its world; going round again.");
    }
}

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
