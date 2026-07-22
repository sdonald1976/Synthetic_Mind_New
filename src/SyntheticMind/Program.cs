using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

// SyntheticMind — the mind itself, not a pipeline you invoke (ARCHITECTURE §5). One executable, one
// continuous heartbeat: it perceives two senses, learns from prediction error, binds what it hears to
// what it sees, remembers across restarts, and — when it recognises a sight it has a sound for —
// SPEAKS, with a voice it taught itself by babbling. Then it does it again. Forever, until Ctrl-C.
//
//   dotnet run --project src/SyntheticMind -- [folder-of-videos]
//
// It watches a folder of videos on an endless loop (its "world"), getting a little better each pass.
// Everything is unsupervised, local, online — no labels, no backprop. This is the whole project,
// running as one living process.

const int SampleRate = 16000, Hop = 160, MelBands = 20, Grid = 12, Orientations = 4, CamW = 120, CamH = 90, FrameStride = 3;

var world = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "temp");
var exts = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".m4v" };
var videos = Directory.Exists(world)
    ? Directory.GetFiles(world).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray()
    : [];
if (videos.Length == 0) { Console.WriteLine($"\n  no world to perceive in {world}. Pass a folder of videos.\n"); return; }

var stateDir = Path.GetFullPath("mind-state");
var saidDir = Path.Combine(stateDir, "said");
Directory.CreateDirectory(saidDir);
var audioCbPath = Path.Combine(stateDir, "audio-codebook.json");
var videoCbPath = Path.Combine(stateDir, "video-codebook.json");
var pairingsPath = Path.Combine(stateDir, "pairings.json");

// --- the mind's persistent faculties ----------------------------------------------------------
var cochlea = new Cochlea(SampleRate, 512, MelBands);
var audioL0 = new Unit(new LearnedPredictiveRule(MelBands, stateWidth: 8, history: 8, quadraticFeatures: 0));
var retina = new Retina(Grid, motion: true, orientations: Orientations, color: true);
var visualWidth = retina.Width;
var videoL0 = new Unit(new LearnedPredictiveRule(visualWidth, stateWidth: 12, history: 6, quadraticFeatures: 0));

var audioVq = VectorQuantizer.LoadOrNew(audioCbPath, capacity: 48, newUnitThreshold: 0.20f);
var videoVq = VectorQuantizer.LoadOrNew(videoCbPath, capacity: 64, newUnitThreshold: 0.30f, subtractRunningMean: true);
if (videoVq.Count > 0 && videoVq.Dimension != visualWidth)   // retina changed since last life → start fresh
{ audioVq = new VectorQuantizer(48, 0.20f); videoVq = new VectorQuantizer(64, 0.30f, subtractRunningMean: true); }
var binder = CrossSituationalBinder.LoadOrNew(pairingsPath);

// The mouth: it learns to speak by babbling before it ever opens its eyes.
var synth = new FormantSynth(SampleRate);
var mouth = new VocalBabbler(synth.ControlCount, MelBands, c => MelOf(synth.Synthesize(c, 3600)), seed: 1, normalizeMel: true);
Console.WriteLine("\n  SyntheticMind — waking up. babbling to learn its own voice...");
for (var i = 0; i < 400; i++) mouth.Babble();

// --- graceful shutdown: on Ctrl-C, finish the thought and remember ----------------------------
var alive = true;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; alive = false; Console.WriteLine("\n  ...going to sleep. remembering."); };

void Remember()
{
    audioVq.Save(audioCbPath);
    videoVq.Save(videoCbPath);
    binder.Save(pairingsPath);
}

Console.WriteLine($"  awake. watching {videos.Length} video(s) in {world} on a loop. Ctrl-C to sleep.");
if (binder.Episodes > 0) Console.WriteLine($"  (remembering a past life: {audioVq.Count} sounds, {videoVq.Count} sights, {binder.Episodes} bindings)");
Console.WriteLine();

// --- the heartbeat: perceive → learn → bind → (recognise → speak), forever ---------------------
var audioSummary = new float[MelBands];
float audioBaseline = 1e-4f, videoBaseline = 1e-4f;
long tick = 0, spoken = 0;
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

        using var frame = new Mat();
        using var small = new Mat();
        var luma = new float[CamW * CamH];
        var red = new float[CamW * CamH];
        var green = new float[CamW * CamH];
        var blue = new float[CamW * CamH];
        var bgr = new byte[CamW * CamH * 3];
        var samplePos = 0; var hopsDone = 0; var frameIndex = 0;
        var buf = new float[512];

        while (alive)
        {
            var process = frameIndex % FrameStride == 0;
            var ok = process ? cap.Read(frame) : cap.Grab();
            if (!ok || (process && frame.Empty())) break;
            frameIndex++;
            if (!process) continue;

            var tMs = cap.Get(VideoCaptureProperties.PosMsec);

            // Hear: catch the ear up to now, learning to predict each moment of sound.
            var targetHop = (int)(tMs / 1000.0 * SampleRate / Hop);
            var audioEvent = false;
            while (hopsDone < targetHop && samplePos + 512 <= samples.Length)
            {
                Array.Copy(buf, Hop, buf, 0, 512 - Hop);
                for (var i = 512 - Hop; i < 512; i++) buf[i] = samples[samplePos++];
                var mel = cochlea.Process(buf);
                var s = audioL0.Observe(mel).SquaredError;
                audioBaseline += 0.001f * (s - audioBaseline);
                for (var i = 0; i < MelBands; i++) audioSummary[i] += 0.05f * (mel[i] - audioSummary[i]);
                if (s > 2f * audioBaseline) audioEvent = true;
                hopsDone++;
            }

            // See: this frame → colour retina → learn to predict it.
            Cv2.Resize(frame, small, new Size(CamW, CamH));
            Marshal.Copy(small.Data, bgr, 0, bgr.Length);
            for (var i = 0; i < CamW * CamH; i++)
            {
                float b = bgr[i * 3] / 255f, g = bgr[i * 3 + 1] / 255f, r = bgr[i * 3 + 2] / 255f;
                blue[i] = b; green[i] = g; red[i] = r;
                luma[i] = 0.114f * b + 0.587f * g + 0.299f * r;
            }
            var feat = retina.Process(luma, CamW, CamH, red, green, blue);
            var vs = videoL0.Observe(feat).SquaredError;
            videoBaseline += 0.01f * (vs - videoBaseline);
            var videoEvent = vs > 2f * videoBaseline;

            // Bind: when both senses spike together, learn that this sight goes with this sound.
            if (audioEvent && videoEvent)
            {
                var au = audioVq.Quantize(audioSummary);
                var vu = videoVq.Quantize(feat);
                binder.Observe([au], [vu]);

                // Recognise & speak: if this sight has a sound bound to it, say that sound — see→say,
                // live, inside the loop. Rate-limited so it doesn't babble over itself.
                if (tick - lastSpeakTick > 300)
                {
                    var recalled = binder.HeardForSeen(vu, minJointCount: 4);
                    if (recalled is { } r && r.Heard < audioVq.Count)
                    {
                        var (control, _, _) = mouth.Imitate(audioVq.Prototype(r.Heard));
                        WavWriter.WriteMono(Path.Combine(saidDir, $"utt{spoken:D4}.wav"), synth.Synthesize(control, 3600), SampleRate);
                        Console.WriteLine($"  [tick {tick,7}] sees sight #{vu} — knows its sound (#{r.Heard}, bound {r.JointCount}x) — speaks → said/utt{spoken:D4}.wav");
                        lastSpeakTick = tick; spoken++;
                    }
                }
            }

            tick++;
            if (tick % 2000 == 0)
            {
                Console.WriteLine($"  [tick {tick,7}] pass {pass} · {audioVq.Count} sounds / {videoVq.Count} sights / {binder.Episodes} bindings · surprise ear {audioBaseline:F3} eye {videoBaseline:F3} · spoken {spoken}x");
                Remember();
            }
        }
    }
    Console.WriteLine($"  [tick {tick,7}] — finished a full pass over its world; going round again.");
}

Remember();
Console.WriteLine($"  asleep. lived {tick} moments, learned {audioVq.Count} sounds + {videoVq.Count} sights, spoke {spoken}x. memory in {stateDir}\\\n");

// --- the ear's summary of a produced/heard sound ----------------------------------------------
float[] MelOf(float[] wave)
{
    var acc = new float[MelBands]; var n = 0;
    for (var p = 0; p + 512 <= wave.Length; p += Hop)
    {
        var mel = cochlea.Process(wave[p..(p + 512)]);
        for (var i = 0; i < MelBands; i++) acc[i] += mel[i];
        n++;
    }
    if (n > 0) for (var i = 0; i < MelBands; i++) acc[i] /= n;
    return acc;
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
