using SyntheticMind.Audio;
using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 016 — Tier 1 of unsupervised sound-unit discovery. A stream of 5 distinct synthesized
/// "syllables" in random order and random durations. Two things must work with no labels:
///   1. surprise (level 0's prediction error) peaks at the true boundaries — segmentation
///   2. the chunks between boundaries cluster into the true 5 types — discovery
/// Self-contained (synthesized), so it needs no downloaded audio.
/// </summary>
public class SyllableDiscoveryTests
{
    private static readonly float[][] Formants =
    [
        [300f, 900f], [500f, 1600f], [450f, 2200f], [700f, 1100f], [380f, 2600f],
    ];

    [Fact]
    public void Surprise_segments_and_chunks_cluster_into_the_true_units()
    {
        const int sampleRate = 16000, hop = 160;
        var rng = new Random(1);

        // Build the syllable stream and remember each sample's true type.
        var samples = new List<float>();
        var typePerSample = new List<int>();
        var phase = new float[2];
        for (var s = 0; s < 500; s++)
        {
            var type = rng.Next(5);
            var durFrames = rng.Next(15, 31);   // random duration → boundaries are not periodic
            for (var i = 0; i < durFrames * hop; i++)
            {
                var v = 0f;
                for (var k = 0; k < 2; k++) { phase[k] += 2f * MathF.PI * Formants[type][k] / sampleRate; v += MathF.Sin(phase[k]); }
                samples.Add(v * 0.5f);
                typePerSample.Add(type);
            }
        }

        var total = samples.Count / hop;
        var pos = 0;
        float[] Pull()
        {
            var b = new float[hop];
            for (var i = 0; i < hop && pos < samples.Count; i++) b[i] = samples[pos++];
            return b;
        }

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

        // --- segmentation: surprise peaks vs true (type-change) boundaries ---
        var trueBoundaries = new List<int>();
        for (var t = warm + 1; t < total; t++) if (typeAt[t] != typeAt[t - 1]) trueBoundaries.Add(t);

        double mean = 0; var n = 0;
        for (var t = warm; t < total; t++) { mean += surprise[t]; n++; }
        mean /= n;
        double variance = 0;
        for (var t = warm; t < total; t++) { var d = surprise[t] - mean; variance += d * d; }
        var sd = Math.Sqrt(variance / n);

        var detected = new List<int>();
        for (var t = warm + 1; t < total - 1; t++)
            if (surprise[t] > surprise[t - 1] && surprise[t] >= surprise[t + 1] && surprise[t] > mean + 0.5 * sd)
                detected.Add(t);

        const int tol = 3;
        var recallHits = trueBoundaries.Count(b => detected.Any(d => Math.Abs(d - b) <= tol));
        var recall = (float)recallHits / Math.Max(1, trueBoundaries.Count);

        // --- discovery: chunk-average mel → k-means(5) → purity vs true type ---
        var chunks = new List<float[]>();
        var chunkType = new List<int>();
        var start = warm;
        for (var t = warm + 1; t <= total; t++)
        {
            if (t == total || typeAt[t] != typeAt[t - 1])
            {
                if (t - start >= 4)
                {
                    var avg = new float[20];
                    for (var f = start + 2; f < t; f++) for (var j = 0; j < 20; j++) avg[j] += mel[f][j];
                    for (var j = 0; j < 20; j++) avg[j] /= t - start - 2;
                    chunks.Add(avg);
                    chunkType.Add(typeAt[start]);
                }
                start = t;
            }
        }

        var purity = KMeansPurity(chunks, chunkType, k: 5, seed: 7);

        Assert.True(recall > 0.7f, $"surprise should find most boundaries, recall {recall:F2}");
        Assert.True(purity > 0.6f, $"chunks should cluster into the true units, purity {purity:F2}");
    }

    private static float KMeansPurity(List<float[]> data, List<int> trueLabels, int k, int seed)
    {
        var dim = data[0].Length;
        var rng = new Random(seed);
        var centroids = new float[k][];
        for (var c = 0; c < k; c++) centroids[c] = (float[])data[rng.Next(data.Count)].Clone();
        var assign = new int[data.Count];

        for (var iter = 0; iter < 40; iter++)
        {
            for (var i = 0; i < data.Count; i++)
            {
                var best = 0; var bestD = float.MaxValue;
                for (var c = 0; c < k; c++)
                {
                    var d = 0f;
                    for (var j = 0; j < dim; j++) { var e = data[i][j] - centroids[c][j]; d += e * e; }
                    if (d < bestD) { bestD = d; best = c; }
                }
                assign[i] = best;
            }
            for (var c = 0; c < k; c++)
            {
                var sum = new float[dim]; var cnt = 0;
                for (var i = 0; i < data.Count; i++) if (assign[i] == c) { for (var j = 0; j < dim; j++) sum[j] += data[i][j]; cnt++; }
                if (cnt > 0) for (var j = 0; j < dim; j++) centroids[c][j] = sum[j] / cnt;
            }
        }

        var correct = 0;
        for (var c = 0; c < k; c++)
        {
            var counts = new int[k];
            for (var i = 0; i < data.Count; i++) if (assign[i] == c) counts[trueLabels[i]]++;
            var majority = 0;
            for (var t = 1; t < k; t++) if (counts[t] > counts[majority]) majority = t;
            for (var i = 0; i < data.Count; i++) if (assign[i] == c && trueLabels[i] == majority) correct++;
        }
        return (float)correct / data.Count;
    }
}
