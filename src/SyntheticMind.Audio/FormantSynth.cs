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
///   [0] F0      — pitch,        90–220 Hz
///   [1] F1      — vowel height, 250–900 Hz
///   [2] F2      — vowel front,   700–2600 Hz
///   [3] voicing — 1 = pure tone (vowel), 0 = pure noise (fricative), between = breathy/voiced-fricative
///
/// The noise source (finding 040) is what lets it make consonants at all: fricatives are turbulent
/// noise shaped by the vocal tract, so the same formant peaks that colour a vowel colour the noise
/// into an "shh"/"ss"-like sound. The noise is deterministic (fixed-seed) so identical controls give
/// an identical waveform — the babbler and its tests depend on that.
/// </summary>
public sealed class FormantSynth
{
    private readonly int _sampleRate;
    private const float FormantBandwidth = 130f;   // Hz — how sharp each formant peak is

    public FormantSynth(int sampleRate = 16000) => _sampleRate = sampleRate;

    /// <summary>Number of control knobs (F0, F1, F2, voicing).</summary>
    public int ControlCount => 4;

    public float[] Synthesize(float[] controls, int samples)
    {
        if (controls.Length != ControlCount)
            throw new ArgumentException($"Expected {ControlCount} controls, got {controls.Length}.", nameof(controls));

        var f0 = Lerp(90f, 220f, Clamp01(controls[0]));
        var f1 = Lerp(250f, 900f, Clamp01(controls[1]));
        var f2 = Lerp(700f, 2600f, Clamp01(controls[2]));
        var voicing = Clamp01(controls[3]);

        // Voiced source: formant-shaped harmonics of F0 (glottal 1/k rolloff), by additive synthesis.
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

        // Unvoiced source: white noise (fixed seed → deterministic) shaped by resonators at F1 and F2.
        var noise = new float[samples];
        var seed = 12345u;
        for (var n = 0; n < samples; n++) { seed = seed * 1664525u + 1013904223u; noise[n] = ((seed >> 8) & 0xFFFFFF) / 8388608f - 1f; }
        var shaped = new float[samples];
        Bandpass(noise, shaped, f1, 5f);
        Bandpass(noise, shaped, f2, 8f);

        var wave = new float[samples];
        for (var n = 0; n < samples; n++)
        {
            var voiced = 0f;
            for (var k = 1; k <= maxK; k++) { voiced += amp[k] * MathF.Sin(phase[k]); phase[k] += inc[k]; }
            wave[n] = voicing * voiced + (1f - voicing) * 3f * shaped[n];
        }

        ApplyEnvelope(wave);
        Normalize(wave, 0.9f);
        return wave;
    }

    /// <summary>
    /// Synthesize a TRAJECTORY (finding 041): the controls change over time, sweeping through a
    /// sequence of keyframes. This is what a syllable IS — "shh-ah" is noise→vowel, "ba" is a
    /// burst→vowel — a path through (F0, F1, F2, voicing) space, not a held point. Rendered block by
    /// block with continuous harmonic phase and continuous noise/filter state, so the sound glides
    /// rather than clicking between frames.
    /// </summary>
    public float[] SynthesizeTrajectory(float[][] keyframes, int samples)
    {
        if (keyframes.Length == 0) throw new ArgumentException("Need at least one keyframe.", nameof(keyframes));
        foreach (var k in keyframes)
            if (k.Length != ControlCount) throw new ArgumentException($"Each keyframe needs {ControlCount} controls.", nameof(keyframes));

        const int Block = 160;
        var wave = new float[samples];
        var phase = new float[81];                       // persistent harmonic phases
        var seed = 12345u;
        float x1a = 0, x2a = 0, y1a = 0, y2a = 0, x1b = 0, x2b = 0, y1b = 0, y2b = 0;   // persistent biquad state

        for (var start = 0; start < samples; start += Block)
        {
            var end = Math.Min(start + Block, samples);
            var c = InterpKeyframes(keyframes, (start + Block * 0.5f) / samples);
            var f0 = Lerp(90f, 220f, Clamp01(c[0]));
            var f1 = Lerp(250f, 900f, Clamp01(c[1]));
            var f2 = Lerp(700f, 2600f, Clamp01(c[2]));
            var voicing = Clamp01(c[3]);

            var maxK = Math.Min(80, (int)((_sampleRate / 2f) / f0));
            var amp = new float[maxK + 1];
            var inc = new float[maxK + 1];
            for (var k = 1; k <= maxK; k++) { amp[k] = (1f / k) * (Resonance(k * f0, f1) + 0.8f * Resonance(k * f0, f2)); inc[k] = 2f * MathF.PI * k * f0 / _sampleRate; }

            var (b0a, b2a, a1a, a2a) = BiquadCoeffs(f1, 5f);
            var (b0b, b2b, a1b, a2b) = BiquadCoeffs(f2, 8f);

            for (var n = start; n < end; n++)
            {
                var v = 0f;
                for (var k = 1; k <= maxK; k++) { v += amp[k] * MathF.Sin(phase[k]); phase[k] += inc[k]; }

                seed = seed * 1664525u + 1013904223u;
                var noise = ((seed >> 8) & 0xFFFFFF) / 8388608f - 1f;
                var ya = b0a * noise + b2a * x2a - a1a * y1a - a2a * y2a; x2a = x1a; x1a = noise; y2a = y1a; y1a = ya;
                var yb = b0b * noise + b2b * x2b - a1b * y1b - a2b * y2b; x2b = x1b; x1b = noise; y2b = y1b; y1b = yb;

                wave[n] = voicing * v + (1f - voicing) * 3f * (ya + yb);
            }
        }

        ApplyEnvelope(wave);
        Normalize(wave, 0.9f);
        return wave;
    }

    private static float[] InterpKeyframes(float[][] keyframes, float t)
    {
        if (keyframes.Length == 1) return keyframes[0];
        var pos = Math.Clamp(t, 0f, 1f) * (keyframes.Length - 1);
        var i = Math.Clamp((int)pos, 0, keyframes.Length - 2);
        var frac = pos - i;
        var c = new float[keyframes[0].Length];
        for (var j = 0; j < c.Length; j++) c[j] = keyframes[i][j] * (1 - frac) + keyframes[i + 1][j] * frac;
        return c;
    }

    private (float B0, float B2, float A1, float A2) BiquadCoeffs(float centerHz, float q)
    {
        var w0 = 2f * MathF.PI * centerHz / _sampleRate;
        var alpha = MathF.Sin(w0) / (2f * q);
        var a0 = 1f + alpha;
        return (alpha / a0, -alpha / a0, -2f * MathF.Cos(w0) / a0, (1f - alpha) / a0);
    }

    // A 2-pole resonant bandpass (RBJ biquad), run over the noise and ADDED into `output` — one call
    // per formant, so the noise ends up with the same spectral peaks that shape the voiced sound.
    private void Bandpass(float[] input, float[] output, float centerHz, float q)
    {
        var w0 = 2f * MathF.PI * centerHz / _sampleRate;
        var cw = MathF.Cos(w0);
        var alpha = MathF.Sin(w0) / (2f * q);
        float a0 = 1f + alpha, b0 = alpha / a0, b2 = -alpha / a0, a1 = -2f * cw / a0, a2 = (1f - alpha) / a0;
        float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (var n = 0; n < input.Length; n++)
        {
            var x = input[n];
            var y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            output[n] += y;
        }
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
