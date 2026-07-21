using System.Runtime.InteropServices;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

// see → say: show it a picture, and it speaks. It perceives the scene (the same colour retina +
// video codebook it learned by watching), recalls the sound that scene was BOUND to across the
// corpus (the cross-situational PMI pairings), and reproduces that sound with the vocal tract it
// taught itself by babbling. Perception of a sight driving speech — grounded end to end, no labels
// anywhere in the chain.
//   dotnet run --project src/SyntheticMind.SeeSay

// Retina config MUST match SyntheticMind.Watch exactly, or a frame maps to different units.
const int Grid = 12, Orientations = 4, CamW = 120, CamH = 90;
const int SampleRate = 16000, FftSize = 512, MelBands = 20, Hop = 160, ClipSamples = 4000;

var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var watchBin = Path.Combine(repo, "src", "SyntheticMind.Watch", "bin", "Debug", "net9.0");
var videoCodebookPath = Path.Combine(watchBin, "watch-video-codebook.json");
var pairingsPath = Path.Combine(watchBin, "watch-pairings.json");
var videoExemplars = Path.Combine(repo, "exemplars", "video");
var audioExemplars = Path.Combine(repo, "exemplars", "audio");
var outDir = Path.Combine(repo, "voice-out", "seesay");

if (!File.Exists(videoCodebookPath) || !File.Exists(pairingsPath) || !Directory.Exists(videoExemplars))
{
    Console.WriteLine("\n  need the watcher's learned state first. Run:");
    Console.WriteLine("    dotnet run --project src/SyntheticMind.Watch -- youtube --exemplars\n");
    return;
}

Console.WriteLine("\n  SyntheticMind — see → say (perceive a scene, speak the sound bound to it)\n");

var videoVq = VectorQuantizer.Load(videoCodebookPath);
var binder = CrossSituationalBinder.Load(pairingsPath);
var retina = new Retina(Grid, motion: true, orientations: Orientations, color: true);
var synth = new FormantSynth(SampleRate);
var cochlea = new Cochlea(SampleRate, FftSize, MelBands);

// Teach the mouth (babble), then it's ready to say whatever it recalls.
float[] Hear(float[] controls) => MelOf(synth.Synthesize(controls, ClipSamples));
var babbler = new VocalBabbler(synth.ControlCount, MelBands, Hear, seed: 1, normalizeMel: true);
for (var i = 0; i < 600; i++) babbler.Babble();

if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
Directory.CreateDirectory(outDir);

// Show it each discovered scene's own frame; perceive it, recall the bound sound, say it.
var shown = new HashSet<int>();
var showcased = new List<(int Seen, int Heard, int Joint, double Pmi, float Closer, string Frame)>();
foreach (var unitDir in Directory.GetDirectories(videoExemplars).OrderBy(d => d))
{
    var framePath = Directory.GetFiles(unitDir, "ex0.jpg").FirstOrDefault();
    if (framePath is null) continue;

    var feat = PerceiveImage(framePath);
    if (feat is null) continue;
    var seen = videoVq.Match(feat).Unit;                 // which scene-unit does it perceive?
    if (seen < 0 || !shown.Add(seen)) continue;          // one showcase per distinct perceived scene

    var pairing = binder.HeardForSeen(seen, minJointCount: 5);   // the sound bound to that scene
    if (pairing is null) continue;
    var (heard, pmi, joint) = pairing.Value;

    var clip = Path.Combine(audioExemplars, $"u{heard:D2}", "ex0.wav");
    if (!File.Exists(clip)) continue;

    var targetMel = MelOf(WavReader.ReadMono(clip, SampleRate));
    var (control, _, dist) = babbler.Imitate(targetMel);
    var chance = babbler.ChanceDistance(targetMel);
    var closer = chance > 1e-6f ? 100f * (1f - dist / chance) : 0f;

    // Save the triple: what it saw, the sound it recalled, and the sound it said.
    File.Copy(framePath, Path.Combine(outDir, $"scene{seen:D2}-seen.jpg"), overwrite: true);
    File.Copy(clip, Path.Combine(outDir, $"scene{seen:D2}-recalled.wav"), overwrite: true);
    WavWriter.WriteMono(Path.Combine(outDir, $"scene{seen:D2}-said.wav"), synth.Synthesize(control, ClipSamples), SampleRate);

    showcased.Add((seen, heard, joint, pmi, closer, framePath));
}

if (showcased.Count == 0)
{
    Console.WriteLine("  no scene had a strong enough bound sound to speak (need pairings with support >= 5).\n");
    return;
}

foreach (var s in showcased.OrderByDescending(s => s.Joint).Take(8))
    Console.WriteLine($"  saw scene-unit {s.Seen,2}  ->  recalled sound-unit {s.Heard,2} (bound {s.Joint}x, PMI {s.Pmi:F2})  ->  said it ({s.Closer:F0}% closer than chance)");

Console.WriteLine($"\n  {showcased.Count} scenes perceived and spoken. triples (seen.jpg / recalled.wav / said.wav) in {outDir}\\\n");

// --- perceive one image exactly as the watcher did: colour resize -> R/G/B + luma -> retina ------
float[]? PerceiveImage(string path)
{
    using var img = Cv2.ImRead(path, ImreadModes.Color);
    if (img.Empty()) return null;
    using var small = new Mat();
    Cv2.Resize(img, small, new Size(CamW, CamH));
    var bgr = new byte[CamW * CamH * 3];
    Marshal.Copy(small.Data, bgr, 0, bgr.Length);

    var luma = new float[CamW * CamH];
    var red = new float[CamW * CamH];
    var green = new float[CamW * CamH];
    var blue = new float[CamW * CamH];
    for (var i = 0; i < CamW * CamH; i++)
    {
        float b = bgr[i * 3] / 255f, g = bgr[i * 3 + 1] / 255f, r = bgr[i * 3 + 2] / 255f;
        blue[i] = b; green[i] = g; red[i] = r;
        luma[i] = 0.114f * b + 0.587f * g + 0.299f * r;
    }
    retina.Reset();   // a still image has no motion history
    return retina.Process(luma, CamW, CamH, red, green, blue);
}

float[] MelOf(float[] wave)
{
    var acc = new float[MelBands];
    var n = 0;
    for (var p = 0; p + FftSize <= wave.Length; p += Hop)
    {
        var mel = cochlea.Process(wave[p..(p + FftSize)]);
        for (var i = 0; i < MelBands; i++) acc[i] += mel[i];
        n++;
    }
    if (n > 0) for (var i = 0; i < MelBands; i++) acc[i] /= n;
    return acc;
}
