using SyntheticMind.Audio;
using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// End-to-end: a real waveform through the real cochlea into the hierarchy. Proves the audio
/// front-end actually plugs into everything findings 001–011 built, on genuine samples rather than
/// abstract vectors. The audio here has the same nested-timescale shape as the synthetic streams —
/// a pitch that alternates slowly — but it arrives as a waveform the cochlea has to decode.
/// </summary>
public class AudioPipelineTests
{
    [Fact]
    public void The_hierarchy_hears_pitch_through_the_cochlea()
    {
        const int sampleRate = 16000;
        const int hop = 160;               // 100 mel-frames per second
        const int switchSamples = 4800;    // pitch flips every 0.3s (~30 frames)

        var cochlea = new Cochlea(sampleRate, fftSize: 512, melBands: 20);

        // A waveform whose pitch alternates between 300 Hz and 1200 Hz. `pitch` is the hidden label.
        var phase = 0f;
        var sampleIndex = 0;
        var pitch = 0;
        float[] Pull()
        {
            var block = new float[hop];
            for (var i = 0; i < hop; i++)
            {
                if (sampleIndex % switchSamples == 0) pitch = 1 - pitch;
                var hz = pitch == 0 ? 300f : 1200f;
                phase += 2f * MathF.PI * hz / sampleRate;
                block[i] = MathF.Sin(phase);
                sampleIndex++;
            }
            return block;
        }

        var audio = new AudioStream(Pull, cochlea, hop);
        // Pitch is a LINEAR feature in the mel spectrum (which band is hot), so no quad features —
        // they'd just add high-variance distractors here. The encoder rate is now the DEFAULT: NLMS
        // normalization (finding 013) makes one rate work across synthetic and audio, no hand-tuning.
        var level0 = new Unit(new LearnedPredictiveRule(cochlea.MelBands, stateWidth: 6, history: 8, quadraticFeatures: 0));

        var states = new List<float[]>();
        var pitches = new List<float>();
        const int frames = 20_000;
        const int window = 12_000;
        for (var t = 0; t < frames; t++)
        {
            var mel = audio.Next();
            var state = level0.Observe(mel).State;
            if (t < frames - window) continue;
            states.Add(state);
            pitches.Add(pitch);
        }

        // Level 0's state should track which pitch is playing — it hears the tone through the cochlea.
        Assert.True(MaxAbsCorrelation(states, pitches) > 0.6f, "level 0 should recover the pitch from real audio");
    }

    private static float MaxAbsCorrelation(List<float[]> states, List<float> target)
    {
        var width = states[0].Length;
        var best = 0f;
        for (var d = 0; d < width; d++)
        {
            double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
            var n = states.Count;
            for (var i = 0; i < n; i++)
            {
                double x = states[i][d], y = target[i];
                sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
            }
            var cov = sxy - sx * sy / n;
            var vx = sxx - sx * sx / n;
            var vy = syy - sy * sy / n;
            if (vx <= 1e-12 || vy <= 1e-12) continue;
            var r = (float)Math.Abs(cov / Math.Sqrt(vx * vy));
            if (r > best) best = r;
        }
        return best;
    }
}
