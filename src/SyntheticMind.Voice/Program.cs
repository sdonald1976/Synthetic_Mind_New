using SyntheticMind.Audio;
using SyntheticMind.Mind;

// The mouth: it learns to make sounds the way a baby does — babble, hear yourself, then reproduce
// what you hear. No labels, no target speech to copy weights from; just a vocal tract, the same ear
// it perceives with, and learning from the error between what it tried and what it heard.
//   dotnet run --project src/SyntheticMind.Voice
//
// Writes WAVs to voice-out/ so you can actually LISTEN to it: a few babbles, then, for each target,
// the target next to the system's best imitation of it with its own voice.

const int SampleRate = 16000, FftSize = 512, MelBands = 20, Hop = 160;
const int ClipSamples = 4000;   // 0.25 s per utterance

var synth = new FormantSynth(SampleRate);
var cochlea = new Cochlea(SampleRate, FftSize, MelBands);
var outDir = Path.GetFullPath("voice-out");
Directory.CreateDirectory(outDir);

// Hear a set of controls: synthesize, then average the mel over the sustained middle of the clip —
// "what I heard myself say". The SAME cochlea used for perceiving the world.
float[] Hear(float[] controls) => MelOf(synth.Synthesize(controls, ClipSamples));

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

// --- BRIDGE MODE: try to SAY the sound-units it discovered by watching video --------------------
// Point it at the exemplar audio clips the watcher saved (the real sounds behind each discovered
// audio unit) and it babbles, then tries to reproduce each one with its own vocal tract. This is
// the two halves of the project meeting: perception's discovered sounds become the mouth's targets.
//   dotnet run --project src/SyntheticMind.Voice -- --say [exemplars/audio]
var sayIdx = Array.IndexOf(args, "--say");
if (sayIdx >= 0)
{
    var clipsDir = sayIdx + 1 < args.Length && !args[sayIdx + 1].StartsWith("--")
        ? Path.GetFullPath(args[sayIdx + 1]) : Path.GetFullPath("exemplars/audio");
    RunBridge(clipsDir);
    return;
}

Console.WriteLine("\n  SyntheticMind — learning to speak by babbling\n");

// --- 1. Babble: try random controls, hear the result, learn the forward model -------------------
var babbler = new VocalBabbler(synth.ControlCount, MelBands, Hear, seed: 1);
const int Babbles = 400;
float earlyErr = 0, lateErr = 0; int earlyN = 0, lateN = 0;
for (var b = 0; b < Babbles; b++)
{
    var err = babbler.Babble();
    if (b < Babbles / 5) { earlyErr += err; earlyN++; }
    else if (b >= Babbles * 4 / 5) { lateErr += err; lateN++; }
}
Console.WriteLine($"  babbled {Babbles}x. forward-model error (predicting its own voice): {earlyErr / earlyN:F3} -> {lateErr / lateN:F3}");
Console.WriteLine("  (it's learning what its own mouth does — error falls as the map firms up)\n");

// Save a few babbles so you can hear the "voice".
float[] RandControls(Random r) { var c = new float[synth.ControlCount]; for (var i = 0; i < c.Length; i++) c[i] = (float)r.NextDouble(); return c; }

var demo = new Random(7);
for (var i = 0; i < 3; i++)
    WavWriter.WriteMono(Path.Combine(outDir, $"babble{i}.wav"), synth.Synthesize(RandControls(demo), ClipSamples), SampleRate);

// --- 2. Imitate its OWN kind of sound (a held-out random target — a solution provably exists) ----
Console.WriteLine("  imitating held-out target sounds (its own vocal range):");
var teacher = new Random(99);
float totalImit = 0, totalChance = 0;
for (var t = 0; t < 4; t++)
{
    var targetControl = RandControls(teacher);
    var targetMel = Hear(targetControl);
    var (control, _, dist) = babbler.Imitate(targetMel);
    var chance = babbler.ChanceDistance(targetMel);
    totalImit += dist; totalChance += chance;
    Console.WriteLine($"    target {t}: imitation distance {dist:F3}  vs chance {chance:F3}  ({100 * (1 - dist / chance):F0}% closer)");
    WavWriter.WriteMono(Path.Combine(outDir, $"target{t}.wav"), synth.Synthesize(targetControl, ClipSamples), SampleRate);
    WavWriter.WriteMono(Path.Combine(outDir, $"imitation{t}.wav"), synth.Synthesize(control, ClipSamples), SampleRate);
}
Console.WriteLine($"  overall: {100 * (1 - totalImit / totalChance):F0}% closer than a random attempt\n");

// --- 3. Imitate a REAL heard sound: approximate a human voice with its own vocal tract -----------
var jfk = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "testaudio", "jfk.wav");
jfk = Path.GetFullPath(jfk);
if (File.Exists(jfk))
{
    var samples = WavReader.ReadMono(jfk, SampleRate);
    // A voiced window ~1.2 s in (into the speech, not the leading silence).
    var start = Math.Min(samples.Length - ClipSamples, (int)(1.2 * SampleRate));
    if (start > 0)
    {
        var window = samples[start..(start + ClipSamples)];
        var targetMel = MelOf(window);
        var (control, _, dist) = babbler.Imitate(targetMel);
        var chance = babbler.ChanceDistance(targetMel);
        Console.WriteLine("  imitating a REAL human voice (a slice of JFK) with its own vocal tract:");
        Console.WriteLine($"    distance {dist:F3}  vs chance {chance:F3}  ({100 * (1 - dist / chance):F0}% closer)");
        Console.WriteLine("    (it can't match a human exactly — different instrument — but it aims its formants at it)");
        WavWriter.WriteMono(Path.Combine(outDir, "real-target.wav"), window, SampleRate);
        WavWriter.WriteMono(Path.Combine(outDir, "real-imitation.wav"), synth.Synthesize(control, ClipSamples), SampleRate);
    }
}

Console.WriteLine($"\n  done. listen in {outDir}\\  (babble*, target*/imitation*, real-target/real-imitation)\n");

// The bridge: babble, then try to reproduce each discovered sound-unit with its own vocal tract.
void RunBridge(string clipsDir)
{
    if (!Directory.Exists(clipsDir))
    {
        Console.WriteLine($"\n  no discovered-sound clips at {clipsDir}");
        Console.WriteLine("  run the watcher with --exemplars first:\n    dotnet run --project src/SyntheticMind.Watch -- youtube --exemplars\n");
        return;
    }

    Console.WriteLine("\n  SyntheticMind — trying to SAY the sounds it discovered by watching\n");

    // Timbre matching (spectral shape, not loudness): the discovered sounds are a different
    // instrument at a different level; "saying the same sound" means matching the shape.
    var babbler = new VocalBabbler(synth.ControlCount, MelBands, Hear, seed: 1, normalizeMel: true);
    for (var i = 0; i < 600; i++) babbler.Babble();

    var sayDir = Path.Combine(outDir, "say");
    if (Directory.Exists(sayDir)) Directory.Delete(sayDir, recursive: true);
    Directory.CreateDirectory(sayDir);

    var results = new List<(string Unit, float Closer, float[] Control, string ClipPath)>();
    foreach (var unitDir in Directory.GetDirectories(clipsDir).OrderBy(d => d))
    {
        var clip = Directory.GetFiles(unitDir, "ex0.wav").FirstOrDefault();
        if (clip is null) continue;
        var wave = WavReader.ReadMono(clip, SampleRate);
        if (wave.Length < FftSize) continue;
        var targetMel = MelOf(wave);
        var energy = 0f; foreach (var m in targetMel) energy += m;
        if (energy < 1e-3f) continue;   // skip near-silent clips

        var (control, _, dist) = babbler.Imitate(targetMel);
        var chance = babbler.ChanceDistance(targetMel);
        var closer = chance > 1e-6f ? 100f * (1f - dist / chance) : 0f;
        results.Add((Path.GetFileName(unitDir), closer, control, clip));
    }

    if (results.Count == 0) { Console.WriteLine("  no usable clips found.\n"); return; }

    results.Sort((a, b) => b.Closer.CompareTo(a.Closer));
    Console.WriteLine($"  babbled 600x, then tried to say {results.Count} discovered sound-units.");
    Console.WriteLine($"  average {results.Average(r => r.Closer):F0}% closer than chance; " +
                      $"{results.Count(r => r.Closer > 30f)}/{results.Count} it can approximate well (>30% closer).\n");
    Console.WriteLine("  best-said units (the vowel-like ones its vocal tract can actually make):");
    foreach (var r in results.Take(6)) Console.WriteLine($"    unit {r.Unit}: {r.Closer:F0}% closer");

    foreach (var r in results.Take(5))
    {
        File.Copy(r.ClipPath, Path.Combine(sayDir, $"{r.Unit}-heard.wav"), overwrite: true);
        WavWriter.WriteMono(Path.Combine(sayDir, $"{r.Unit}-said.wav"), synth.Synthesize(r.Control, ClipSamples), SampleRate);
    }
    Console.WriteLine($"\n  wrote heard-vs-said pairs to {sayDir}\\  (each unit as discovered, then as spoken)\n");
}
