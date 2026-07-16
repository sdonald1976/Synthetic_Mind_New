using SyntheticMind.Audio;
using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 017 — Tier 2. 20 synthesized syllable types WITH per-instance variation, and the count
/// is NOT given: novelty-gated online prototypes (the Slice-0 fast store) decide how many units
/// there are. Pins the robust claims — it scales, the prototypes are pure, and the count comes out
/// the same ORDER as the truth (it over-splits rather than collapsing or exploding). Recovering the
/// exact count is granularity-dependent and left to the finding, not asserted.
/// </summary>
public class SyllableCountTests
{
    [Fact]
    public void Discovers_a_sensible_number_of_pure_units_without_being_told_the_count()
    {
        const int sampleRate = 16000, hop = 160;
        const int trueCount = 20;

        var formants = new List<float[]>();
        foreach (var f1 in new[] { 300f, 450f, 600f, 750f })
            foreach (var f2 in new[] { 1000f, 1500f, 2000f, 2500f, 3000f })
                formants.Add([f1, f2]);

        var rng = new Random(1);
        var samples = new List<float>();
        var typePerSample = new List<int>();
        var phase = new float[2];
        for (var s = 0; s < 1600; s++)
        {
            var type = rng.Next(trueCount);
            var durFrames = rng.Next(15, 31);
            float j0 = 1f + (float)(rng.NextDouble() * 0.12 - 0.06), j1 = 1f + (float)(rng.NextDouble() * 0.12 - 0.06);
            for (var i = 0; i < durFrames * hop; i++)
            {
                phase[0] += 2f * MathF.PI * formants[type][0] * j0 / sampleRate;
                phase[1] += 2f * MathF.PI * formants[type][1] * j1 / sampleRate;
                samples.Add((MathF.Sin(phase[0]) + MathF.Sin(phase[1])) * 0.5f);
                typePerSample.Add(type);
            }
        }

        var total = samples.Count / hop;
        var pos = 0;
        float[] Pull() { var b = new float[hop]; for (var i = 0; i < hop && pos < samples.Count; i++) b[i] = samples[pos++]; return b; }

        var cochlea = new Cochlea(sampleRate, 512, 20);
        var audio = new AudioStream(Pull, cochlea, hop, normalize: false);
        var level0 = new Unit(new LearnedPredictiveRule(20, stateWidth: 8, history: 8, quadraticFeatures: 0));

        var surprise = new float[total];
        var mel = new float[total][];
        var typeAt = new int[total];
        for (var t = 0; t < total; t++)
        {
            typeAt[t] = typePerSample[Math.Min(t * hop, typePerSample.Count - 1)];
            mel[t] = audio.Next();
            surprise[t] = level0.Observe(mel[t]).SquaredError;
        }

        var warm = total / 3;
        double mean = 0; var n = 0;
        for (var t = warm; t < total; t++) { mean += surprise[t]; n++; }
        mean /= n;
        double variance = 0;
        for (var t = warm; t < total; t++) { var d = surprise[t] - mean; variance += d * d; }
        var sd = Math.Sqrt(variance / n);

        var trueBoundaries = new List<int>();
        for (var t = warm + 1; t < total; t++) if (typeAt[t] != typeAt[t - 1]) trueBoundaries.Add(t);
        var detected = new HashSet<int>();
        for (var t = warm + 1; t < total - 1; t++)
            if (surprise[t] > surprise[t - 1] && surprise[t] >= surprise[t + 1] && surprise[t] > mean + 0.5 * sd) detected.Add(t);
        var recallHits = trueBoundaries.Count(b => Enumerable.Range(b - 3, 7).Any(detected.Contains));
        var recall = (float)recallHits / trueBoundaries.Count;

        // Chunks → normalized mean mel.
        var chunks = new List<float[]>();
        var chunkType = new List<int>();
        var start = warm;
        for (var t = warm + 1; t <= total; t++)
        {
            if (t == total || detected.Contains(t))
            {
                if (t - start >= 4)
                {
                    var avg = new float[20];
                    for (var f = start + 2; f < t; f++) for (var j = 0; j < 20; j++) avg[j] += mel[f][j];
                    var norm = MathF.Sqrt(avg.Sum(x => x * x)) + 1e-6f;
                    for (var j = 0; j < 20; j++) avg[j] /= norm;
                    chunks.Add(avg);
                    var h = new int[trueCount];
                    for (var f = start; f < t; f++) h[typeAt[f]]++;
                    chunkType.Add(Array.IndexOf(h, h.Max()));
                }
                start = t;
            }
        }

        // Novelty-gated online prototypes — the count emerges from the vigilance, not a given k.
        const float vigilance = 0.25f;
        var protos = new List<float[]>();
        var hist = new List<int[]>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var best = -1; var bestD = float.MaxValue;
            for (var p = 0; p < protos.Count; p++)
            {
                var d = 0f;
                for (var j = 0; j < 20; j++) { var e = chunks[i][j] - protos[p][j]; d += e * e; }
                d = MathF.Sqrt(d);
                if (d < bestD) { bestD = d; best = p; }
            }
            if (best < 0 || bestD > vigilance)
            {
                protos.Add((float[])chunks[i].Clone());
                var hh = new int[trueCount]; hh[chunkType[i]] = 1; hist.Add(hh);
            }
            else
            {
                for (var j = 0; j < 20; j++) protos[best][j] += 0.1f * (chunks[i][j] - protos[best][j]);
                hist[best][chunkType[i]]++;
            }
        }

        var pure = hist.Sum(h => h.Max());
        var purity = (float)pure / hist.Sum(h => h.Sum());

        Assert.True(recall > 0.65f, $"segmentation should hold at 20 units, recall {recall:F2}");
        Assert.True(purity > 0.6f, $"discovered prototypes should be pure, purity {purity:F2}");
        // Same order as the truth: it over-splits (finding 017), but doesn't collapse to 1 or explode.
        Assert.InRange(protos.Count, trueCount, trueCount * 8);
    }
}
