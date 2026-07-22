using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

// object → word (finding 037): the hardest step, toward NAMES. Instead of binding a whole scene to a
// whole audio clip (see→say), this attends to a salient OBJECT (a sub-region that pops from the
// background) and segments a WORD (a voiced run bounded by pauses) out of continuous speech, then
// binds object-units to word-units with the same cross-situational PMI. If the same object recurs
// with the same word across the corpus, the pairing rises. Child-directed speech (slow, with pauses
// around isolated words) is what makes the word half tractable.
//   dotnet run --project src/SyntheticMind.Name -- <folder>

const int SampleRate = 16000, Hop = 160, Fft = 512, MelBands = 20;
const int CamW = 120, CamH = 90, FoveaGrid = 8, Orientations = 4;
const int FrameStride = 3;

var folder = Path.GetFullPath(args.Length > 0 ? args[0] : "youtube");
var exts = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".m4v" };
var videos = Directory.Exists(folder)
    ? Directory.GetFiles(folder).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray()
    : [];
Console.WriteLine($"\n  SyntheticMind — learning object↔word in {videos.Length} video(s) in {folder}\n");
if (videos.Length == 0) { Console.WriteLine("  no videos found.\n"); return; }

var cochlea = new Cochlea(SampleRate, Fft, MelBands);
var attention = new ObjectAttention(new Retina(FoveaGrid, motion: false, orientations: Orientations, color: true),
    mode: AttentionMode.PersonCentred);
var objectVq = new VectorQuantizer(capacity: 64, newUnitThreshold: 0.30f, subtractRunningMean: true);
var wordVq = new VectorQuantizer(capacity: 48, newUnitThreshold: 0.20f);
var binder = new CrossSituationalBinder();

// closest exemplars per unit — object crops (jpg) and word clips (wav) — so we can see/hear what a unit is.
const int PerUnit = 6;
var objEx = new Dictionary<int, List<(float Dist, byte[] Jpg)>>();
var wordEx = new Dictionary<int, List<(float Dist, float[] Clip)>>();
void KeepObj(int u, float d, byte[] jpg) { Keep(objEx, u, (d, jpg), x => x.Item1); }
void KeepWord(int u, float d, float[] clip) { Keep(wordEx, u, (d, clip), x => x.Item1); }
static void Keep<T>(Dictionary<int, List<T>> store, int u, T item, Func<T, float> dist)
{
    if (u < 0) return;
    if (!store.TryGetValue(u, out var l)) store[u] = l = [];
    l.Add(item);
    if (l.Count > PerUnit) { l.Sort((a, b) => dist(a).CompareTo(dist(b))); l.RemoveAt(l.Count - 1); }
}

foreach (var video in videos)
{
    var name = Path.GetFileName(video);
    var samples = ExtractAudio(video, SampleRate);
    using var cap = new VideoCapture(video);
    if (!cap.IsOpened()) { Console.WriteLine($"  {name}: could not open, skipping"); continue; }

    var seg = new WordSegmenter(MelBands);
    var buf = new float[Fft];
    var samplePos = 0; var hopsDone = 0;
    int words = 0, binds = 0, frameIndex = 0, currentObject = -1;

    using var frame = new Mat();
    using var small = new Mat();
    var luma = new float[CamW * CamH];
    var red = new float[CamW * CamH];
    var green = new float[CamW * CamH];
    var blue = new float[CamW * CamH];
    var bgr = new byte[CamW * CamH * 3];

    while (true)
    {
        var process = frameIndex % FrameStride == 0;
        var ok = process ? cap.Read(frame) : cap.Grab();
        if (!ok || (process && frame.Empty())) break;
        frameIndex++;
        if (!process) continue;

        var tMs = cap.Get(VideoCaptureProperties.PosMsec);

        // Attend to an object in this frame → object-unit; keep the attended crop as an exemplar.
        Cv2.Resize(frame, small, new Size(CamW, CamH));
        Marshal.Copy(small.Data, bgr, 0, bgr.Length);
        for (var i = 0; i < CamW * CamH; i++)
        {
            float b = bgr[i * 3] / 255f, g = bgr[i * 3 + 1] / 255f, r = bgr[i * 3 + 2] / 255f;
            blue[i] = b; green[i] = g; red[i] = r;
            luma[i] = 0.114f * b + 0.587f * g + 0.299f * r;
        }
        var (feat, x0, y0, wW, hH) = attention.Attend(luma, red, green, blue, CamW, CamH);
        currentObject = objectVq.Quantize(feat);
        KeepObj(currentObject, objectVq.DistanceTo(currentObject, feat), CropJpg(frame, x0, y0, wW, hH));

        // Catch audio up to this frame; segment words; bind each to the object attended right now.
        var targetHop = (int)(tMs / 1000.0 * SampleRate / Hop);
        while (hopsDone < targetHop && samplePos + Fft <= samples.Length)
        {
            Array.Copy(buf, Hop, buf, 0, Fft - Hop);
            var energy = 0f;
            for (var i = Fft - Hop; i < Fft; i++) { buf[i] = samples[samplePos++]; energy += buf[i] * buf[i]; }
            energy /= Hop;
            var mel = cochlea.Process(buf);
            var word = seg.Accept(mel, energy);
            hopsDone++;
            if (word is null || currentObject < 0) continue;

            var wordUnit = wordVq.Quantize(word);
            binder.Observe([wordUnit], [currentObject]);
            KeepWord(wordUnit, wordVq.DistanceTo(wordUnit, word), ClipAround(samples, SampleRate, tMs, 0.6));
            words++; binds++;
        }
    }
    Console.WriteLine($"  {name,-28} {words,4} words -> {objectVq.Count} object / {wordVq.Count} word units");
}

Console.WriteLine($"\n  done. {binder.Episodes} object↔word co-occurrences, {objectVq.Count} object + {wordVq.Count} word units.");
var top = binder.TopPairings(12, minJointCount: 4);
Console.WriteLine("  strongest object↔word pairings (cross-situational PMI):");
foreach (var (h, s, pmi, joint) in top)
    Console.WriteLine($"    word #{h,-2} <-> object #{s,-2}   pmi {pmi,5:F2}, seen together {joint}x");

WriteExemplars(Path.GetFullPath("exemplars-ow"));
Console.WriteLine($"\n  exemplars (object crops + word clips) in exemplars-ow\\\n");

// --- helpers ----------------------------------------------------------------------------------
byte[] CropJpg(Mat frame, int x0, int y0, int wW, int hH)
{
    // bbox is in CamW×CamH space; scale to the original frame and crop.
    var sx = frame.Width / (float)CamW; var sy = frame.Height / (float)CamH;
    var rect = new Rect((int)(x0 * sx), (int)(y0 * sy), Math.Max(1, (int)(wW * sx)), Math.Max(1, (int)(hH * sy)));
    rect = rect.Intersect(new Rect(0, 0, frame.Width, frame.Height));
    using var crop = new Mat(frame, rect);
    Cv2.ImEncode(".jpg", crop, out var jpg, [(int)ImwriteFlags.JpegQuality, 82]);
    return jpg;
}

void WriteExemplars(string dir)
{
    foreach (var (kind, _) in new[] { ("object", 0), ("word", 0) })
    {
        var p = Path.Combine(dir, kind);
        if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
        Directory.CreateDirectory(p);
    }
    foreach (var (u, l) in objEx)
    {
        l.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        var ud = Path.Combine(dir, "object", $"u{u:D2}"); Directory.CreateDirectory(ud);
        for (var i = 0; i < l.Count; i++) File.WriteAllBytes(Path.Combine(ud, $"ex{i}.jpg"), l[i].Jpg);
    }
    foreach (var (u, l) in wordEx)
    {
        l.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        var ud = Path.Combine(dir, "word", $"u{u:D2}"); Directory.CreateDirectory(ud);
        for (var i = 0; i < l.Count; i++) if (l[i].Clip.Length > 0) WavWriter.WriteMono(Path.Combine(ud, $"ex{i}.wav"), l[i].Clip, SampleRate);
    }
}

static float[] ClipAround(float[] samples, int rate, double tMs, double seconds)
{
    var c = (int)(tMs / 1000.0 * rate); var half = (int)(seconds * rate / 2);
    var s = Math.Max(0, c - half); var e = Math.Min(samples.Length, c + half);
    if (e <= s) return [];
    var clip = new float[e - s]; Array.Copy(samples, s, clip, 0, e - s); return clip;
}

static float[] ExtractAudio(string video, int rate)
{
    var tmp = Path.Combine(Path.GetTempPath(), $"sm-ow-{Guid.NewGuid():N}.wav");
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
