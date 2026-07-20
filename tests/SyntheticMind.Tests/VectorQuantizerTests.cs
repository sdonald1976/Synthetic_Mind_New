using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 029 — bounded consolidation. The watcher over-split on real video (~10k "concepts" from
/// 39 clips) because every slightly-different event minted a new one. A bounded codebook is the
/// fix: it can never exceed its capacity, and things that recur land on the same unit id.
/// </summary>
public class VectorQuantizerTests
{
    private static float[] Direction(int dim, int which, Random rng)
    {
        // A vector clustered around a fixed random centre `which`, with a little noise — successive
        // draws for the same `which` are near each other, different `which` are far apart.
        var centre = new Random(1000 + which);
        var v = new float[dim];
        for (var i = 0; i < dim; i++) v[i] = (float)(centre.NextDouble() - 0.5) + 0.05f * (float)(rng.NextDouble() - 0.5);
        return v;
    }

    [Fact]
    public void Codebook_never_exceeds_capacity_no_matter_how_many_novel_vectors()
    {
        var rng = new Random(7);
        var vq = new VectorQuantizer(capacity: 16, newUnitThreshold: 0.05f); // very eager to split
        for (var i = 0; i < 5000; i++)
        {
            // Every vector a fresh random direction — without the cap this would mint thousands.
            var v = new float[32];
            for (var j = 0; j < v.Length; j++) v[j] = (float)(rng.NextDouble() - 0.5);
            vq.Quantize(v);
        }
        Assert.True(vq.Count <= 16, $"codebook must respect its ceiling, got {vq.Count}");
        Assert.Equal(16, vq.Count); // and it fills up
    }

    [Fact]
    public void Recurring_vectors_consolidate_onto_a_stable_unit()
    {
        var rng = new Random(3);
        var vq = new VectorQuantizer(capacity: 64, newUnitThreshold: 0.15f);

        // Five real "things" seen over and over in random order. We expect roughly five units, and
        // the SAME thing to keep quantizing to the SAME id — that's consolidation, not explosion.
        var firstId = new Dictionary<int, int>();
        var stable = 0;
        var total = 0;
        for (var step = 0; step < 2000; step++)
        {
            var thing = rng.Next(5);
            var id = vq.Quantize(Direction(32, thing, rng));
            if (!firstId.ContainsKey(thing)) firstId[thing] = id;
            else { total++; if (firstId[thing] == id) stable++; }
        }

        Assert.True(vq.Count <= 10, $"five recurring things should make ~a handful of units, got {vq.Count}");
        Assert.True((float)stable / total > 0.95f, $"a recurring thing should keep the same id, got {(float)stable / total:P0}");
    }

    [Fact]
    public void Running_mean_subtraction_breaks_the_low_variance_collapse()
    {
        // The finding-030 failure, reproduced: every event is a big shared "typical frame" plus a
        // small scene-specific pattern. Raw, the vectors all point almost the same way (the shared
        // part dominates) and collapse onto ONE unit — which is exactly what the video side did on
        // Ms Rachel (98% of events on a single unit). Centering against the running mean cancels the
        // shared part, so the distinct scenes separate.
        const int dim = 60, classes = 6;
        var shared = new float[dim];
        var s = new Random(99);
        for (var i = 0; i < dim; i++) shared[i] = 5f + (float)s.NextDouble();   // large, always-present
        var bumps = new float[classes][];
        for (var c = 0; c < classes; c++)
        {
            bumps[c] = new float[dim];
            bumps[c][c * (dim / classes)] = 1.5f;                                // small, scene-specific
        }
        float[] Sample(int c, Random r)
        {
            var v = (float[])shared.Clone();
            for (var i = 0; i < dim; i++) v[i] += bumps[c][i] + 0.02f * (float)(r.NextDouble() - 0.5);
            return v;
        }

        var rng = new Random(4);
        var raw = new VectorQuantizer(capacity: 64, newUnitThreshold: 0.15f, subtractRunningMean: false);
        var centered = new VectorQuantizer(capacity: 64, newUnitThreshold: 0.15f, subtractRunningMean: true);
        for (var t = 0; t < 3000; t++)
        {
            var c = rng.Next(classes);
            var v = Sample(c, rng);
            raw.Quantize(v);
            centered.Quantize((float[])v.Clone());
        }

        Assert.True(raw.Count <= 2, $"raw baseline-dominated vectors should collapse, got {raw.Count} units");
        Assert.True(centered.Count >= classes, $"centering should recover the {classes} scenes, got {centered.Count} units");
    }

    [Fact]
    public void Survives_a_save_load_round_trip()
    {
        var rng = new Random(5);
        var vq = new VectorQuantizer(capacity: 32, newUnitThreshold: 0.15f);
        for (var i = 0; i < 500; i++) vq.Quantize(Direction(24, rng.Next(4), rng));

        var path = Path.Combine(Path.GetTempPath(), $"vq-{Guid.NewGuid():N}.json");
        try
        {
            vq.Save(path);
            var reloaded = VectorQuantizer.Load(path);
            Assert.Equal(vq.Count, reloaded.Count);
            // A vector from a known cluster classifies to the same unit before and after reload.
            var probe = Direction(24, 2, rng);
            Assert.Equal(vq.Classify(probe), reloaded.Classify(probe));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
