namespace SyntheticMind.Audio;

/// <summary>
/// The vocal tract: a few control knobs in, a waveform out — the motor counterpart to the
/// <see cref="Cochlea"/>'s ear. It is dumb and fixed (it learns nothing); learning to <em>use</em> it
/// is the job of the babbler. Deliberately simple, but real: an additive source-filter model where a
/// glottal buzz (harmonics of a fundamental F0) is shaped by two formant resonances F1/F2. Formants
/// are what distinguish vowels — F1/F2 <em>are</em> the vowel space — so different knob settings make
/// audibly different vowel-like sounds, which is exactly what gives the babbler something to learn.
///
/// Controls are all in [0, 1] (a clean space to babble in) and map to:
///   [0] F0  — pitch,        90–220 Hz
///   [1] F1  — vowel height, 250–900 Hz
///   [2] F2  — vowel front,   700–2600 Hz
/// </summary>
public sealed class FormantSynth
{
    private readonly int _sampleRate;
    private const float FormantBandwidth = 130f;   // Hz — how sharp each formant peak is

    public FormantSynth(int sampleRate = 16000) => _sampleRate = sampleRate;

    /// <summary>Number of control knobs (F0, F1, F2).</summary>
    public int ControlCount => 3;

    public float[] Synthesize(float[] controls, int samples)
    {
        if (controls.Length != ControlCount)
            throw new ArgumentException($"Expected {ControlCount} controls, got {controls.Length}.", nameof(controls));

        var f0 = Lerp(90f, 220f, Clamp01(controls[0]));
        var f1 = Lerp(250f, 900f, Clamp01(controls[1]));
        var f2 = Lerp(700f, 2600f, Clamp01(controls[2]));

        // Precompute each harmonic's amplitude (glottal 1/k rolloff, shaped by the two formants) and
        // phase increment, so the per-sample loop is just an accumulate.
        var maxK = Math.Min(80, (int)((_sampleRate / 2f) / f0));
        var amp = new float[maxK + 1];
        var phase = new float[maxK + 1];
        var inc = new float[maxK + 1];
        for (var k = 1; k <= maxK; k++)
        {
            var fk = k * f0;
            amp[k] = (1f / k) * (Resonance(fk, f1) + 0.8f * Resonance(fk, f2));
            inc[k] = 2f * MathF.PI * fk / _sampleRate;
        }

        var wave = new float[samples];
        for (var n = 0; n < samples; n++)
        {
            var s = 0f;
            for (var k = 1; k <= maxK; k++)
            {
                s += amp[k] * MathF.Sin(phase[k]);
                phase[k] += inc[k];
            }
            wave[n] = s;
        }

        ApplyEnvelope(wave);
        Normalize(wave, 0.9f);
        return wave;
    }

    // A formant is a resonance peak; a harmonic near it is boosted (Gaussian bump in frequency).
    private static float Resonance(float hz, float centerHz)
    {
        var d = (hz - centerHz) / FormantBandwidth;
        return MathF.Exp(-0.5f * d * d);
    }

    // Raised-cosine attack/release so the clip doesn't start or end on a click.
    private static void ApplyEnvelope(float[] wave)
    {
        var edge = Math.Min(wave.Length / 4, wave.Length / 10 + 1);
        for (var i = 0; i < edge; i++)
        {
            var g = 0.5f - 0.5f * MathF.Cos(MathF.PI * i / edge);
            wave[i] *= g;
            wave[wave.Length - 1 - i] *= g;
        }
    }

    private static void Normalize(float[] wave, float peak)
    {
        var max = 0f;
        foreach (var s in wave) max = MathF.Max(max, MathF.Abs(s));
        if (max < 1e-6f) return;
        var gain = peak / max;
        for (var i = 0; i < wave.Length; i++) wave[i] *= gain;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
}
