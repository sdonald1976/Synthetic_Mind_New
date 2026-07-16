using SyntheticMind.Audio;
using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 015, self-contained. On real audio (speech ↔ music) the source identity lives in the
/// perception but the learned encoder discards it — so a slow level must pool the PERCEPTION, not
/// the encoder's output, and its clock must match the source's timescale. This reproduces that with
/// two synthesized sources (distinct spectra) so it needs no downloaded files.
/// </summary>
public class ScenePerceptionTests
{
    [Fact]
    public void A_slow_level_over_perception_recovers_the_slow_source()
    {
        const int sampleRate = 16000;
        const int hop = 160;
        const int switchSamples = 2 * sampleRate;   // source alternates every 2 seconds

        // Two "sources" with clearly different spectra: a low tone and a high tone.
        var phase = 0f;
        var index = 0;
        var source = 0;
        float[] Pull()
        {
            var block = new float[hop];
            for (var i = 0; i < hop; i++)
            {
                if (index > 0 && index % switchSamples == 0) source = 1 - source;
                var hz = source == 0 ? 300f : 2500f;
                phase += 2f * MathF.PI * hz / sampleRate;
                block[i] = MathF.Sin(phase);
                index++;
            }
            return block;
        }

        var cochlea = new Cochlea(sampleRate, fftSize: 512, melBands: 20);
        var audio = new AudioStream(Pull, cochlea, hop, normalize: false);   // keep the source signal
        var slow = new TemporalLevel(inputWidth: 20, stride: 10, integratorRate: 0.3f);  // clock ~matched to 2s

        var slowStates = new List<float[]>();
        var sources = new List<float>();
        const int frames = 8000;    // 80s
        const int warmup = 3000;
        for (var t = 0; t < frames; t++)
        {
            var mel = audio.Next();
            var s = slow.Observe(mel);
            if (t < warmup) continue;
            slowStates.Add(s);
            sources.Add(source);
        }

        // The slow level should form a stable representation of WHICH source is playing.
        Assert.True(MaxAbsCorrelation(slowStates, sources) > 0.6f,
            "a slow level over perception should recover the slow source identity");
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
