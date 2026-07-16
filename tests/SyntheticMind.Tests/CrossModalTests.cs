using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 021 — cross-modal grounding, the rung-4 target. Five "objects", each a distinct SOUND
/// (tone pair) paired with a distinct SIGHT (moving shape). They're presented together, unlabeled,
/// and a <see cref="CrossModalStore"/> binds each co-occurring pair. The test: hearing a sound
/// recalls the right sight, and seeing a sight recalls the right sound — meaning bound across senses
/// with no teacher, only co-occurrence. Self-contained (both modalities synthesized here).
/// </summary>
public class CrossModalTests
{
    private const int SampleRate = 16000, Hop = 160, Size = 32, Grid = 8, Clip = 24, Objects = 5;
    private static readonly float[][] Formants =
        [[300f, 900f], [500f, 1600f], [450f, 2200f], [700f, 1100f], [380f, 2600f]];

    [Fact]
    public void Binds_sound_to_sight_by_co_occurrence_and_recalls_across_senses()
    {
        var rng = new Random(1);
        var store = new CrossModalStore();

        // Present objects together, unlabeled — bind whatever co-occurs.
        for (var i = 0; i < 60; i++)
        {
            var c = rng.Next(Objects);
            store.Bind(Sound(c, rng), Sight(c, rng));
        }

        // Reference per-object signatures (for scoring only — the store never saw these labels).
        var refAudio = new float[Objects][];
        var refVisual = new float[Objects][];
        for (var c = 0; c < Objects; c++)
        {
            refAudio[c] = new float[20];
            refVisual[c] = new float[Grid * Grid * 2];
            var rr = new Random(99 + c);
            for (var i = 0; i < 12; i++)
            {
                var a = Sound(c, rr); var v = Sight(c, rr);
                for (var j = 0; j < 20; j++) refAudio[c][j] += a[j] / 12;
                for (var j = 0; j < refVisual[c].Length; j++) refVisual[c][j] += v[j] / 12;
            }
        }

        int okAudioToVisual = 0, okVisualToAudio = 0, total = 0;
        for (var c = 0; c < Objects; c++)
            for (var i = 0; i < 12; i++)
            {
                var recalledVisual = store.RecallVisual(Sound(c, rng))!;
                if (NearestRef(recalledVisual, refVisual) == c) okAudioToVisual++;

                var recalledAudio = store.RecallAudio(Sight(c, rng))!;
                if (NearestRef(recalledAudio, refAudio) == c) okVisualToAudio++;
                total++;
            }

        Assert.True((float)okAudioToVisual / total > 0.8f, $"hear→see recall {100f * okAudioToVisual / total:F0}%");
        Assert.True((float)okVisualToAudio / total > 0.8f, $"see→hear recall {100f * okVisualToAudio / total:F0}%");
    }

    // Scale-invariant nearest reference: the store returns normalized centroids while the reference
    // signatures are raw averages, so compare by cosine (normalize both) not raw distance.
    private static int NearestRef(float[] v, float[][] refs)
    {
        var q = Unit(v);
        var best = 0; var bestD = float.MaxValue;
        for (var k = 0; k < refs.Length; k++)
        {
            var r = Unit(refs[k]);
            var d = 0f;
            for (var j = 0; j < q.Length; j++) { var e = q[j] - r[j]; d += e * e; }
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    private static float[] Unit(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x)) + 1e-6f;
        var r = new float[v.Length];
        for (var i = 0; i < v.Length; i++) r[i] = v[i] / norm;
        return r;
    }

    private static float[] Sound(int c, Random r)
    {
        const int frames = 50;
        var samples = new float[frames * Hop];
        var phase = new float[2];
        float j0 = 1 + (float)(r.NextDouble() * 0.1 - 0.05), j1 = 1 + (float)(r.NextDouble() * 0.1 - 0.05);
        for (var i = 0; i < samples.Length; i++)
        {
            phase[0] += 2 * MathF.PI * Formants[c][0] * j0 / SampleRate;
            phase[1] += 2 * MathF.PI * Formants[c][1] * j1 / SampleRate;
            samples[i] = (MathF.Sin(phase[0]) + MathF.Sin(phase[1])) * 0.5f;
        }
        var pos = 0;
        float[] Pull() { var b = new float[Hop]; for (var i = 0; i < Hop && pos < samples.Length; i++) b[i] = samples[pos++]; return b; }
        var audio = new AudioStream(Pull, new Cochlea(SampleRate, 512, 20), Hop, normalize: false);
        var acc = new float[20];
        for (var t = 0; t < frames; t++) { var m = audio.Next(); for (var i = 0; i < 20; i++) acc[i] += m[i] / frames; }
        return acc;
    }

    private static float[] Sight(int c, Random r)
    {
        float speed = 0.8f + (float)r.NextDouble() * 0.4f, ph0 = (float)r.NextDouble();
        float ox = (float)(r.NextDouble() * 4 - 2), oy = (float)(r.NextDouble() * 4 - 2);
        var retina = new Retina(Grid, motion: true);
        var acc = new float[retina.Width]; var n = 0;
        for (var t = 0; t < Clip; t++)
        {
            var f = new float[Size * Size];
            var u = t * speed / Clip + ph0;
            float px = 16 + ox, py = 16 + oy, rad = 4;
            if (c == 0) { px = 4 + u % 1f * 24 + ox; py = 16 + oy; }
            else if (c == 1) { px = 16 + ox; py = 4 + u % 1f * 24 + oy; }
            else if (c == 2) { px = 4 + u % 1f * 24 + ox; py = 4 + u % 1f * 24 + oy; }
            else if (c == 3) { px = 16 + 10 * MathF.Cos(2 * MathF.PI * u) + ox; py = 16 + 10 * MathF.Sin(2 * MathF.PI * u) + oy; }
            if (c == 4) { var b = (MathF.Sin(2 * MathF.PI * u * 2) + 1) / 2 * 0.8f; for (var i = 0; i < f.Length; i++) f[i] = b; }
            else for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++)
            { var d = MathF.Sqrt((x - px) * (x - px) + (y - py) * (y - py)); if (d < rad) f[y * Size + x] = 1f; }
            var feat = retina.Process(f, Size, Size);
            if (t == 0) continue;
            for (var i = 0; i < feat.Length; i++) acc[i] += feat[i];
            n++;
        }
        for (var i = 0; i < acc.Length; i++) acc[i] /= n;
        return acc;
    }
}
