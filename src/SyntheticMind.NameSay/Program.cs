using System.Runtime.InteropServices;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

// THE CAPSTONE: see an object → recall the word bound to it → SAY it. Vision drives speech at word
// grain, and every single link was learned with no labels:
//   - perceive the object    (person-centred attention + object codebook, finding 039)
//   - recall its word         (cross-situational PMI pairing, HeardForSeen)
//   - say that word           (babble-taught vocal tract, as a syllable trajectory, findings 040/041)
// This closes the whole project into one continuous loop.
//   dotnet run --project src/SyntheticMind.NameSay

const int FoveaGrid = 8, Orientations = 4, WinW = 60, WinH = 45;   // must match SyntheticMind.Name's fovea window
const int SampleRate = 16000, FftSize = 512, MelBands = 20, ClipSamples = 4000;
const int K = 3, N = 8;

var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var objectCodebookPath = Path.Combine(repo, "name-object-codebook.json");
var pairingsPath = Path.Combine(repo, "name-pairings.json");
var objectExemplars = Path.Combine(repo, "exemplars-ow", "object");
var wordExemplars = Path.Combine(repo, "exemplars-ow", "word");
var outDir = Path.Combine(repo, "voice-out", "namesay");

if (!File.Exists(objectCodebookPath) || !File.Exists(pairingsPath) || !Directory.Exists(objectExemplars))
{
    Console.WriteLine("\n  need the object→word learned state. Run first:");
    Console.WriteLine("    dotnet run --project src/SyntheticMind.Name -- youtube-veh\n");
    return;
}

Console.WriteLine("\n  SyntheticMind — see an object → recall its word → say it\n");

var objectVq = VectorQuantizer.Load(objectCodebookPath);
var binder = CrossSituationalBinder.Load(pairingsPath);
var fovea = new Retina(FoveaGrid, motion: false, orientations: Orientations, color: true);
var synth = new FormantSynth(SampleRate);
var cochlea = new Cochlea(SampleRate, FftSize, MelBands);

// Teach the mouth (babble syllable-trajectories), then it can say whatever it recalls.
float[][] ToKeys(float[] c)
{
    var keys = new float[K][];
    for (var k = 0; k < K; k++) { keys[k] = new float[synth.ControlCount]; Array.Copy(c, k * synth.ControlCount, keys[k], 0, synth.ControlCount); }
    return keys;
}
float[] MelTrajectory(float[] wave)
{
    var traj = new float[N * MelBands];
    for (var f = 0; f < N; f++)
    {
        var s = Math.Clamp((int)((f + 0.5f) / N * wave.Length) - FftSize / 2, 0, Math.Max(0, wave.Length - FftSize));
        var frame = new float[FftSize];
        Array.Copy(wave, s, frame, 0, Math.Min(FftSize, wave.Length - s));
        var mel = cochlea.Process(frame);
        Array.Copy(mel, 0, traj, f * MelBands, MelBands);
    }
    return traj;
}
var babbler = new VocalBabbler(K * synth.ControlCount, N * MelBands,
    c => MelTrajectory(synth.SynthesizeTrajectory(ToKeys(c), ClipSamples)), seed: 1, normalizeMel: true);
for (var i = 0; i < 800; i++) babbler.Babble();

if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
Directory.CreateDirectory(outDir);

// Show it each object; perceive it, recall the bound word, say it.
var shown = new HashSet<int>();
var showcased = new List<(int Seen, int Word, int Joint, double Pmi, float Closer)>();
foreach (var objDir in Directory.GetDirectories(objectExemplars).OrderBy(d => d))
{
    var framePath = Directory.GetFiles(objDir, "ex0.jpg").FirstOrDefault();
    if (framePath is null) continue;

    var feat = PerceiveObject(framePath);
    if (feat is null) continue;
    var seen = objectVq.Match(feat).Unit;                       // which object-unit does it perceive?
    if (seen < 0 || !shown.Add(seen)) continue;

    var pairing = binder.HeardForSeen(seen, minJointCount: 4);  // the word bound to that object
    if (pairing is null) continue;
    var (word, pmi, joint) = pairing.Value;

    var wordClip = Path.Combine(wordExemplars, $"u{word:D2}", "ex0.wav");
    if (!File.Exists(wordClip)) continue;

    var target = MelTrajectory(WavReader.ReadMono(wordClip, SampleRate));   // the recalled word, as a trajectory
    var (control, _, dist) = babbler.Imitate(target, refineSteps: 120);
    var chance = babbler.ChanceDistance(target);
    var closer = chance > 1e-6f ? 100f * (1f - dist / chance) : 0f;

    File.Copy(framePath, Path.Combine(outDir, $"obj{seen:D2}-seen.jpg"), overwrite: true);
    File.Copy(wordClip, Path.Combine(outDir, $"obj{seen:D2}-recalled-word.wav"), overwrite: true);
    WavWriter.WriteMono(Path.Combine(outDir, $"obj{seen:D2}-said.wav"), synth.SynthesizeTrajectory(ToKeys(control), ClipSamples), SampleRate);
    showcased.Add((seen, word, joint, pmi, closer));
}

if (showcased.Count == 0) { Console.WriteLine("  no object had a strong enough bound word to say (need pairings with support ≥ 4).\n"); return; }

foreach (var s in showcased.OrderByDescending(s => s.Joint).Take(8))
    Console.WriteLine($"  saw object {s.Seen,2}  →  recalled word {s.Word,2} (bound {s.Joint}x, PMI {s.Pmi:F2})  →  said it ({s.Closer:F0}% closer than chance)");

Console.WriteLine($"\n  {showcased.Count} objects perceived and spoken. triples (seen.jpg / recalled-word.wav / said.wav) in {outDir}\\\n");

// Perceive one object crop exactly as the learner's fovea did: resize to the window, colour + luma → retina.
float[]? PerceiveObject(string path)
{
    using var img = Cv2.ImRead(path, ImreadModes.Color);
    if (img.Empty()) return null;
    using var small = new Mat();
    Cv2.Resize(img, small, new Size(WinW, WinH));
    var bgr = new byte[WinW * WinH * 3];
    Marshal.Copy(small.Data, bgr, 0, bgr.Length);

    var luma = new float[WinW * WinH];
    var red = new float[WinW * WinH];
    var green = new float[WinW * WinH];
    var blue = new float[WinW * WinH];
    for (var i = 0; i < WinW * WinH; i++)
    {
        float b = bgr[i * 3] / 255f, g = bgr[i * 3 + 1] / 255f, r = bgr[i * 3 + 2] / 255f;
        blue[i] = b; green[i] = g; red[i] = r;
        luma[i] = 0.114f * b + 0.587f * g + 0.299f * r;
    }
    return fovea.Process(luma, WinW, WinH, red, green, blue);
}
