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
var demo = new Random(7);
for (var i = 0; i < 3; i++)
{
    var c = new[] { (float)demo.NextDouble(), (float)demo.NextDouble(), (float)demo.NextDouble() };
    WavWriter.WriteMono(Path.Combine(outDir, $"babble{i}.wav"), synth.Synthesize(c, ClipSamples), SampleRate);
}

// --- 2. Imitate its OWN kind of sound (a held-out random target — a solution provably exists) ----
Console.WriteLine("  imitating held-out target sounds (its own vocal range):");
var teacher = new Random(99);
float totalImit = 0, totalChance = 0;
for (var t = 0; t < 4; t++)
{
    var targetControl = new[] { (float)teacher.NextDouble(), (float)teacher.NextDouble(), (float)teacher.NextDouble() };
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
