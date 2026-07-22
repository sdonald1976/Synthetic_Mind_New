using SyntheticMind.Audio;
using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 034 — the mouth. A vocal tract it knows nothing about at first; it babbles, hears itself
/// through the same cochlea it perceives with, learns the controls→sound map, and reproduces target
/// sounds. No labels, no target weights — the infant babble-then-imitate loop.
/// </summary>
public class VocalTests
{
    private const int SR = 16000, Fft = 512, Mel = 20, Hop = 160, Clip = 4000;

    private static float[] MelOf(Cochlea c, float[] wave)
    {
        var acc = new float[Mel];
        var n = 0;
        for (var p = 0; p + Fft <= wave.Length; p += Hop)
        {
            var m = c.Process(wave[p..(p + Fft)]);
            for (var i = 0; i < Mel; i++) acc[i] += m[i];
            n++;
        }
        for (var i = 0; i < Mel; i++) acc[i] /= n;
        return acc;
    }

    private static (VocalBabbler Babbler, Func<float[], float[]> Hear) NewBabbler(int seed)
    {
        var synth = new FormantSynth(SR);
        var cochlea = new Cochlea(SR, Fft, Mel);
        float[] Hear(float[] c) => MelOf(cochlea, synth.Synthesize(c, Clip));
        return (new VocalBabbler(synth.ControlCount, Mel, Hear, seed), Hear);
    }

    [Fact]
    public void The_synth_makes_distinguishable_vowels()
    {
        var synth = new FormantSynth(SR);
        var cochlea = new Cochlea(SR, Fft, Mel);

        // Two very different formant settings (an "ee"-ish vs an "aw"-ish region), both fully voiced.
        var a = MelOf(cochlea, synth.Synthesize([0.3f, 0.1f, 0.9f, 1f], Clip));
        var b = MelOf(cochlea, synth.Synthesize([0.3f, 0.9f, 0.1f, 1f], Clip));
        var same = MelOf(cochlea, synth.Synthesize([0.3f, 0.1f, 0.9f, 1f], Clip));

        Assert.Equal(0f, Dist(a, same), 3);                     // deterministic (noise is fixed-seed)
        Assert.True(Dist(a, b) > 2f, $"different formants should sound different, got {Dist(a, b):F2}");
    }

    [Fact]
    public void Voicing_makes_a_consonant_distinct_from_a_vowel()
    {
        var synth = new FormantSynth(SR);
        var cochlea = new Cochlea(SR, Fft, Mel);

        // Same formants, opposite voicing: a voiced vowel vs an unvoiced fricative (noise). They must
        // sound clearly different — that difference is the whole point of a noise source (finding 040).
        var vowel = MelOf(cochlea, synth.Synthesize([0.3f, 0.5f, 0.7f, 1f], Clip));   // voiced
        var fric = MelOf(cochlea, synth.Synthesize([0.3f, 0.5f, 0.7f, 0f], Clip));    // pure noise
        Assert.True(Dist(vowel, fric) > 1.5f, $"a fricative should differ from a vowel, got {Dist(vowel, fric):F2}");

        // And the fricative should carry more of its energy up high (noise) than the vowel (harmonic, low).
        float HighFrac(float[] m) { float hi = 0, all = 1e-6f; for (var i = 0; i < m.Length; i++) { all += m[i]; if (i >= m.Length / 2) hi += m[i]; } return hi / all; }
        Assert.True(HighFrac(fric) > HighFrac(vowel), "the fricative should be more high-frequency than the vowel");
    }

    [Fact]
    public void A_trajectory_changes_over_time_a_held_sound_does_not()
    {
        var synth = new FormantSynth(SR);
        var cochlea = new Cochlea(SR, Fft, Mel);

        // A syllable that sweeps noise → vowel (voicing 0 → 1): the spectrum must CHANGE across it —
        // early noisy/high, late harmonic/low. That change over time is what a syllable is (finding 041).
        var syllable = synth.SynthesizeTrajectory([[0.3f, 0.5f, 0.7f, 0f], [0.3f, 0.5f, 0.7f, 1f]], 6000);
        var early = MelOf(cochlea, syllable[..2048]);
        var late = MelOf(cochlea, syllable[^2048..]);
        var syllableChange = Dist(early, late);
        Assert.True(syllableChange > 1.0f, $"a noise→vowel syllable should change over time, got {syllableChange:F2}");

        // A held sound (identical keyframes) should change far less.
        var held = synth.SynthesizeTrajectory([[0.3f, 0.5f, 0.7f, 1f], [0.3f, 0.5f, 0.7f, 1f]], 6000);
        var heldChange = Dist(MelOf(cochlea, held[..2048]), MelOf(cochlea, held[^2048..]));
        Assert.True(heldChange < syllableChange, $"a held sound ({heldChange:F2}) should change less than a syllable ({syllableChange:F2})");
    }

    [Fact]
    public void Babbling_lowers_the_forward_model_error()
    {
        var (babbler, _) = NewBabbler(seed: 2);
        float early = 0, late = 0; int en = 0, ln = 0;
        for (var i = 0; i < 400; i++)
        {
            var e = babbler.Babble();
            if (i < 80) { early += e; en++; }
            else if (i >= 320) { late += e; ln++; }
        }
        Assert.True(late / ln < early / en, $"forward-model error should fall: {early / en:F2} -> {late / ln:F2}");
    }

    [Fact]
    public void It_imitates_a_target_far_better_than_chance()
    {
        var (babbler, hear) = NewBabbler(seed: 3);
        for (var i = 0; i < 400; i++) babbler.Babble();

        // A held-out target from the same vocal range — a reproducible solution provably exists.
        var targetMel = hear([0.4f, 0.7f, 0.3f, 1f]);
        var (_, _, dist) = babbler.Imitate(targetMel);
        var chance = babbler.ChanceDistance(targetMel);

        Assert.True(dist < 0.4f * chance, $"imitation ({dist:F2}) should be far under chance ({chance:F2})");
    }

    private static float Dist(float[] a, float[] b)
    {
        var d = 0f;
        for (var i = 0; i < a.Length; i++) { var e = a[i] - b[i]; d += e * e; }
        return MathF.Sqrt(d);
    }
}
