using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
//
// EXEMPLAR MODE — read-only, turns the anonymous unit ids into something a human can interpret:
//   dotnet run --project src/SyntheticMind.Watch -- <folder> --exemplars [outdir]
// It replays the videos with the ALREADY-LEARNED codebooks (never mutating them), and for each
// discovered unit saves the handful of real frames / audio clips that best exemplify it, plus an
// index.html laying the strongest sound<->sight pairings side by side. This is how you find out
// whether "audio #16 <-> video #45" is the alphabet song and its on-screen card, or just noise.

const int SampleRate = 16000, Hop = 160, MelBands = 20, Grid = 10, Orientations = 4, CamW = 80, CamH = 60;

var argList = args.ToList();
var exFlag = argList.IndexOf("--exemplars");
var dumpExemplars = exFlag >= 0;
var exemplarDir = dumpExemplars && exFlag + 1 < argList.Count && !argList[exFlag + 1].StartsWith("--")
    ? Path.GetFullPath(argList[exFlag + 1])
    : Path.GetFullPath("exemplars");

var folder = argList.Count > 0 && !argList[0].StartsWith("--") ? argList[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "testvideo");
folder = Path.GetFullPath(folder);
var extensions = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".m4v" };
var videos = Directory.Exists(folder)
    ? Directory.GetFiles(folder).Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray()
    : [];

Console.WriteLine($"\n  SyntheticMind — {(dumpExemplars ? "dumping exemplars from" : "watching")} {videos.Length} video(s) in {folder}\n");
if (videos.Length == 0) { Console.WriteLine("  no videos found. pass a folder: dotnet run -- <folder>\n"); return; }

// Consolidation + cross-situational binding (finding 029). Bounded codebooks fold the messy real
// events into a SMALL set of recurring units instead of minting a fresh "concept" every time (the
// ~10k-concept explosion we watched on 39 videos). A PMI layer over those unit ids then finds the
// sound↔sight pairings that recur across the whole playlist and discounts the ever-present ones
// (Ms Rachel's face is on screen almost constantly, so naive binding pairs it with every sound).
var audioCodebookPath = Path.Combine(AppContext.BaseDirectory, "watch-audio-codebook.json");
var videoCodebookPath = Path.Combine(AppContext.BaseDirectory, "watch-video-codebook.json");
var pairingsPath = Path.Combine(AppContext.BaseDirectory, "watch-pairings.json");

if (dumpExemplars && !(File.Exists(audioCodebookPath) && File.Exists(videoCodebookPath) && File.Exists(pairingsPath)))
{
    Console.WriteLine($"  no learned codebooks found next to the exe. Run the watcher first:\n    dotnet run --project src/SyntheticMind.Watch -- {folder}\n");
    return;
}

var audioVq = VectorQuantizer.LoadOrNew(audioCodebookPath, capacity: 48, newUnitThreshold: 0.20f);
// Video is a low-variance sense (a talking-head kids' show is nearly the same frame every time), so
// it must match on each frame's DEVIATION from the typical frame or it collapses onto one unit
// (finding 030). subtractRunningMean does exactly that.
var videoVq = VectorQuantizer.LoadOrNew(videoCodebookPath, capacity: 64, newUnitThreshold: 0.30f, subtractRunningMean: true);
var binder = CrossSituationalBinder.LoadOrNew(pairingsPath);
if (binder.Episodes > 0)
    Console.WriteLine($"  ({(dumpExemplars ? "using" : "resuming")}: {audioVq.Count} audio + {videoVq.Count} video units, {binder.Episodes} co-occurrences)\n");

// Exemplar mode keeps, per unit, the K closest real frames / audio clips (smallest cosine distance
// to the prototype = the most textbook examples). Bounded so memory stays flat over a long corpus.
const int ExemplarsPerUnit = 6;
var videoExemplars = new Dictionary<int, List<(float Dist, byte[] Jpg, string Source, double TMs)>>();
var audioExemplars = new Dictionary<int, List<(float Dist, float[] Clip, string Source, double TMs)>>();

void KeepVideo(int unit, float dist, byte[] jpg, string src, double tMs)
{
    if (unit < 0) return;
    if (!videoExemplars.TryGetValue(unit, out var list)) videoExemplars[unit] = list = [];
    list.Add((dist, jpg, src, tMs));
    if (list.Count > ExemplarsPerUnit) { list.Sort((a, b) => a.Dist.CompareTo(b.Dist)); list.RemoveAt(list.Count - 1); }
}
void KeepAudio(int unit, float dist, float[] clip, string src, double tMs)
{
    if (unit < 0) return;
    if (!audioExemplars.TryGetValue(unit, out var list)) audioExemplars[unit] = list = [];
    list.Add((dist, clip, src, tMs));
    if (list.Count > ExemplarsPerUnit) { list.Sort((a, b) => a.Dist.CompareTo(b.Dist)); list.RemoveAt(list.Count - 1); }
}

// Shared pipelines — they keep learning across the whole folder, not per file.
var cochlea = new Cochlea(SampleRate, 512, MelBands);
var audioL0 = new Unit(new LearnedPredictiveRule(MelBands, stateWidth: 8, history: 8, quadraticFeatures: 0));
var visualWidth = Grid * Grid * (2 + Orientations);
var retina = new Retina(Grid, motion: true, orientations: Orientations);
var videoL0 = new Unit(new LearnedPredictiveRule(visualWidth, stateWidth: 12, history: 6, quadraticFeatures: 0));

// Rolling audio summary + adaptive "event" detection (a spike above the running baseline). Video
// binds on the instantaneous frame at the event (the quantizer does its own running-mean centering),
// so there is no rolling video summary anymore.
var audioSummary = new float[MelBands];
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
        var videoEvent = vs > 2f * videoBaseline;

        // A co-occurring event (both senses spiking), with a cooldown so a busy stretch doesn't
        // flood it. Same event gating in both modes, so exemplars are drawn from the exact same
        // event population the units were built from.
        if (audioEvent && videoEvent && tMs - lastBindMs > 400)
        {
            if (dumpExemplars)
            {
                // Read-only: find which unit each sense matches and how well (distance), then keep
                // this frame / audio clip if it's among the closest exemplars for that unit.
                var (au, ad) = audioVq.Match(audioSummary);
                var (vu, vd) = videoVq.Match(feat);
                Cv2.ImEncode(".jpg", frame, out var jpg, [(int)ImwriteFlags.JpegQuality, 82]);
                KeepVideo(vu, vd, jpg, name, tMs);
                KeepAudio(au, ad, ClipAround(samples, SampleRate, tMs, 0.7), name, tMs);
            }
            else
            {
                var au = audioVq.Quantize(audioSummary);
                var vu = videoVq.Quantize(feat);   // this frame, not a time-blur — quantizer centers it
                binder.Observe([au], [vu]);
            }
            lastBindMs = tMs;
            bindsThisVideo++;
        }
        frames++;
    }

    if (!dumpExemplars)
    {
        audioVq.Save(audioCodebookPath);
        videoVq.Save(videoCodebookPath);
        binder.Save(pairingsPath);
    }
    var learned = lateN > 0 && earlyN > 0 ? $"surprise {earlyAudio / earlyN:F3} -> {lateAudio / Math.Max(1, lateN):F3}" : "n/a";
    var tail = dumpExemplars ? $"{bindsThisVideo} events sampled" : $"{bindsThisVideo} events -> {audioVq.Count} audio / {videoVq.Count} video units (audio {learned})";
    Console.WriteLine($"  {name,-28} {frames,4} frames, {samples.Length / SampleRate}s audio | {tail}");
}

if (dumpExemplars)
{
    WriteExemplars(exemplarDir, audioExemplars, videoExemplars, audioVq, videoVq, binder, SampleRate);
    Console.WriteLine($"\n  done. wrote exemplars for {videoExemplars.Count} video + {audioExemplars.Count} audio units to {exemplarDir}");
    Console.WriteLine($"  open {Path.Combine(exemplarDir, "index.html")} to see what each unit actually is.\n");
    return;
}

Console.WriteLine($"\n  done. consolidated {binder.Episodes} co-occurrences into {audioVq.Count} audio + {videoVq.Count} video units (bounded — no more 10k-concept pile).");
var top = binder.TopPairings(10, minJointCount: 3);
if (top.Count > 0)
{
    Console.WriteLine("  strongest recurring sound<->sight pairings (cross-situational PMI):");
    foreach (var (h, s, pmi, joint) in top)
        Console.WriteLine($"    audio #{h,-2} <-> video #{s,-2}   pmi {pmi,5:F2}, seen together {joint}x");
}
else
{
    Console.WriteLine("  no pairing recurred often enough to report yet (need more footage).");
}
Console.WriteLine();

// --- a short mono window centred on an event, for auditioning a sound-unit --------------------
static float[] ClipAround(float[] samples, int rate, double tMs, double seconds)
{
    var center = (int)(tMs / 1000.0 * rate);
    var half = (int)(seconds * rate / 2);
    var start = Math.Max(0, center - half);
    var end = Math.Min(samples.Length, center + half);
    if (end <= start) return [];
    var clip = new float[end - start];
    Array.Copy(samples, start, clip, 0, end - start);
    return clip;
}

// --- write the exemplar gallery: closest frames/clips per unit + a browsable index.html -------
static void WriteExemplars(
    string dir,
    Dictionary<int, List<(float Dist, float[] Clip, string Source, double TMs)>> audioEx,
    Dictionary<int, List<(float Dist, byte[] Jpg, string Source, double TMs)>> videoEx,
    VectorQuantizer audioVq, VectorQuantizer videoVq, CrossSituationalBinder binder, int rate)
{
    // Fresh output dirs (this is derived, disposable content).
    foreach (var sub in new[] { "video", "audio" })
    {
        var p = Path.Combine(dir, sub);
        if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
        Directory.CreateDirectory(p);
    }

    // Write each unit's closest exemplars to disk, sorted best-first.
    foreach (var (unit, list) in videoEx)
    {
        list.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        var udir = Path.Combine(dir, "video", $"u{unit:D2}");
        Directory.CreateDirectory(udir);
        for (var i = 0; i < list.Count; i++) File.WriteAllBytes(Path.Combine(udir, $"ex{i}.jpg"), list[i].Jpg);
    }
    foreach (var (unit, list) in audioEx)
    {
        list.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        var udir = Path.Combine(dir, "audio", $"u{unit:D2}");
        Directory.CreateDirectory(udir);
        for (var i = 0; i < list.Count; i++)
            if (list[i].Clip.Length > 0) WavWriter.WriteMono(Path.Combine(udir, $"ex{i}.wav"), list[i].Clip, rate);
    }

    static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
    string Thumbs(int unit) => videoEx.TryGetValue(unit, out var l)
        ? string.Concat(Enumerable.Range(0, l.Count).Select(i =>
            $"<img src=\"video/u{unit:D2}/ex{i}.jpg\" title=\"{Enc(l[i].Source)} @ {l[i].TMs / 1000:F1}s\">"))
        : "<span class=\"none\">no frames</span>";
    string Players(int unit) => audioEx.TryGetValue(unit, out var l)
        ? string.Concat(Enumerable.Range(0, l.Count).Where(i => l[i].Clip.Length > 0).Select(i =>
            $"<audio controls preload=\"none\" src=\"audio/u{unit:D2}/ex{i}.wav\" title=\"{Enc(l[i].Source)} @ {l[i].TMs / 1000:F1}s\"></audio>"))
        : "<span class=\"none\">no clips</span>";

    var sb = new StringBuilder();
    sb.Append("""
    <!doctype html><meta charset="utf-8"><title>SyntheticMind — discovered units</title>
    <style>
      body{background:#14161a;color:#e6e8ec;font:15px/1.5 system-ui,sans-serif;margin:0;padding:24px 32px;max-width:1100px}
      h1{font-size:20px} h2{margin-top:40px;border-bottom:1px solid #2a2e35;padding-bottom:6px}
      .sub{color:#9aa0aa} img{height:96px;border-radius:6px;margin:0 6px 6px 0;background:#000;vertical-align:top}
      .none{color:#6b7280;font-style:italic}
      .pair{background:#1b1e24;border:1px solid #2a2e35;border-radius:10px;padding:14px 16px;margin:14px 0}
      .pair .hd{color:#8bd450;font-weight:600;margin-bottom:8px}
      .cols{display:flex;gap:22px;flex-wrap:wrap} .col{min-width:260px}
      .col h4{margin:0 0 8px;font-size:13px;color:#9aa0aa;font-weight:600}
      audio{height:34px;width:250px;display:block;margin:0 0 6px}
      .unit{background:#1b1e24;border:1px solid #2a2e35;border-radius:10px;padding:12px 14px;margin:10px 0}
      .unit .id{font-weight:600} .unit .cnt{color:#9aa0aa;font-weight:400}
    </style>
    <h1>SyntheticMind — what the discovered units actually are</h1>
    <p class="sub">Unsupervised, from co-occurrence alone. Each unit is a cluster the system formed on its own;
    below are the real frames / audio clips closest to each one. Nothing here was labelled — the pairings are
    whatever recurred across the corpus (cross-situational PMI). Copyrighted footage, local inspection only.</p>
    """);

    sb.Append("<h2>Strongest sound↔sight pairings <span class=\"sub\">— what bound to what</span></h2>");
    var pairings = binder.TopPairings(20, minJointCount: 5);
    if (pairings.Count == 0) sb.Append("<p class=\"none\">no pairing recurred often enough (min 5×).</p>");
    foreach (var (h, s, pmi, joint) in pairings)
    {
        sb.Append($"<div class=\"pair\"><div class=\"hd\">audio #{h} ↔ video #{s} &nbsp; · &nbsp; PMI {pmi:F2} · seen together {joint}×</div>");
        sb.Append("<div class=\"cols\">");
        sb.Append($"<div class=\"col\"><h4>hear it — audio unit #{h}</h4>{Players(h)}</div>");
        sb.Append($"<div class=\"col\"><h4>see it — video unit #{s}</h4>{Thumbs(s)}</div>");
        sb.Append("</div></div>");
    }

    sb.Append("<h2>All video units <span class=\"sub\">— by how often they fired</span></h2>");
    foreach (var unit in videoEx.Keys.OrderByDescending(u => videoVq.CountOf(u)))
        sb.Append($"<div class=\"unit\"><div class=\"id\">video #{unit} <span class=\"cnt\">· {videoVq.CountOf(unit)} events</span></div>{Thumbs(unit)}</div>");

    sb.Append("<h2>All audio units <span class=\"sub\">— by how often they fired</span></h2>");
    foreach (var unit in audioEx.Keys.OrderByDescending(u => audioVq.CountOf(u)))
        sb.Append($"<div class=\"unit\"><div class=\"id\">audio #{unit} <span class=\"cnt\">· {audioVq.CountOf(unit)} events</span></div>{Players(unit)}</div>");

    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "index.html"), sb.ToString());
}

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
