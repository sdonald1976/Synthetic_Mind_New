using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

// Batch watcher: point it at a folder of video files and it watches AND listens to all of them,
// unattended, learning as it goes and remembering across the whole pile.
//   dotnet run --project src/SyntheticMind.Watch -- <folder>
//
// Video frames come from OpenCvSharp; the audio track is pulled out with ffmpeg (must be on PATH).
// The two run in sync; when both senses spike together, it auto-binds the co-occurring pair
// (no human trigger) into a persistent CrossModalStore. This is the finding 021/022 mechanism,
// unsupervised, at file scale.

const int SampleRate = 16000, Hop = 160, MelBands = 20, Grid = 10, Orientations = 4, CamW = 80, CamH = 60;

var folder = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "testvideo");
folder = Path.GetFullPath(folder);
var extensions = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".m4v" };
var videos = Directory.Exists(folder)
    ? Directory.GetFiles(folder).Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray()
    : [];

Console.WriteLine($"\n  SyntheticMind — watching {videos.Length} video(s) in {folder}\n");
if (videos.Length == 0) { Console.WriteLine("  no videos found. pass a folder: dotnet run -- <folder>\n"); return; }

var memoryPath = Path.Combine(AppContext.BaseDirectory, "watch-memory.json");
var store = CrossModalStore.LoadOrNew(memoryPath);
if (store.Count > 0) Console.WriteLine($"  (starting from {store.Count} concept(s) learned earlier)\n");

// Shared pipelines — they keep learning across the whole folder, not per file.
var cochlea = new Cochlea(SampleRate, 512, MelBands);
var audioL0 = new Unit(new LearnedPredictiveRule(MelBands, stateWidth: 8, history: 8, quadraticFeatures: 0));
var visualWidth = Grid * Grid * (2 + Orientations);
var retina = new Retina(Grid, motion: true, orientations: Orientations);
var videoL0 = new Unit(new LearnedPredictiveRule(visualWidth, stateWidth: 12, history: 6, quadraticFeatures: 0));

// Rolling perception summaries + adaptive "event" detection (a spike above the running baseline).
var audioSummary = new float[MelBands];
var videoSummary = new float[visualWidth];
float audioBaseline = 1e-4f, videoBaseline = 1e-4f;

foreach (var video in videos)
{
    var name = Path.GetFileName(video);
    var samples = ExtractAudio(video, SampleRate);
    using var cap = new VideoCapture(video);
    if (!cap.IsOpened()) { Console.WriteLine($"  {name}: could not open, skipping"); continue; }

    var samplePos = 0;
    float[] Pull() { var b = new float[Hop]; for (var i = 0; i < Hop && samplePos < samples.Length; i++) b[i] = samples[samplePos++]; return b; }
    var audio = new AudioStream(Pull, cochlea, Hop, normalize: false);

    using var frame = new Mat();
    using var gray = new Mat();
    using var small = new Mat();
    var pixels = new float[CamW * CamH];
    var bytes = new byte[CamW * CamH];

    int frames = 0, hopsDone = 0, bindsThisVideo = 0;
    float earlyAudio = 0, lateAudio = 0; int earlyN = 0, lateN = 0;
    var lastBindMs = -1000.0;
    var conceptsAtStart = store.Count;

    while (cap.Read(frame) && !frame.Empty())
    {
        var tMs = cap.Get(VideoCaptureProperties.PosMsec);

        // Catch the audio up to this video frame's time.
        var targetHop = (int)(tMs / 1000.0 * SampleRate / Hop);
        var audioEvent = false;
        while (hopsDone < targetHop && samplePos + 512 <= samples.Length)
        {
            var mel = audio.Next();
            var s = audioL0.Observe(mel).SquaredError;
            audioBaseline += 0.001f * (s - audioBaseline);
            for (var i = 0; i < MelBands; i++) audioSummary[i] += 0.05f * (mel[i] - audioSummary[i]);
            if (s > 2f * audioBaseline) audioEvent = true;
            if (hopsDone < 300) { earlyAudio += s; earlyN++; } else { lateAudio += s; lateN++; }
            hopsDone++;
        }

        // Video frame → retina → level 0.
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Resize(gray, small, new Size(CamW, CamH));
        Marshal.Copy(small.Data, bytes, 0, bytes.Length);
        for (var i = 0; i < bytes.Length; i++) pixels[i] = bytes[i] / 255f;
        var feat = retina.Process(pixels, CamW, CamH);
        var vs = videoL0.Observe(feat).SquaredError;
        videoBaseline += 0.01f * (vs - videoBaseline);
        for (var i = 0; i < visualWidth; i++) videoSummary[i] += 0.1f * (feat[i] - videoSummary[i]);
        var videoEvent = vs > 2f * videoBaseline;

        // Auto-bind a co-occurring event (both senses spiking), with a cooldown so a busy stretch
        // doesn't flood the store. Consolidation + cross-situational statistics sort real from noise.
        if (audioEvent && videoEvent && tMs - lastBindMs > 400)
        {
            store.Bind(audioSummary, videoSummary);
            lastBindMs = tMs;
            bindsThisVideo++;
        }
        frames++;
    }

    store.Save(memoryPath);
    var learned = lateN > 0 && earlyN > 0 ? $"surprise {earlyAudio / earlyN:F3} -> {lateAudio / Math.Max(1, lateN):F3}" : "n/a";
    var newConcepts = store.Count - conceptsAtStart;
    Console.WriteLine($"  {name,-28} {frames,4} frames, {samples.Length / SampleRate}s audio | bound {bindsThisVideo} events, +{newConcepts} concepts (audio {learned})");
}

Console.WriteLine($"\n  done. {store.Count} concept(s) total, saved to {Path.GetFileName(memoryPath)}\n");

// --- pull the audio track out of a video with ffmpeg (mono, 16 kHz) --------------------------
static float[] ExtractAudio(string video, int rate)
{
    var tmp = Path.Combine(Path.GetTempPath(), $"sm-audio-{Guid.NewGuid():N}.wav");
    try
    {
        var psi = new ProcessStartInfo("ffmpeg", $"-y -v error -i \"{video}\" -vn -ac 1 -ar {rate} \"{tmp}\"")
        { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi);
        if (p is null) return [];
        p.WaitForExit();
        return File.Exists(tmp) ? WavReader.ReadMono(tmp, rate) : [];
    }
    catch { return []; }   // no ffmpeg, or no audio track → learn visually only
    finally { if (File.Exists(tmp)) File.Delete(tmp); }
}
