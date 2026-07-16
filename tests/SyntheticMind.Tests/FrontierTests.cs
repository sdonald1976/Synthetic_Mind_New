using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Pins finding 007 — the stacking/abstraction frontier. Two claims of very different strength:
/// the construction (level 0 blind to the meta-regime) is ROBUST and asserted firmly; the payoff
/// (a slowed level 1 recovering it) is FRAGILE and asserted only on a known-good seed, so the test
/// documents that it's possible without pretending it's reliable.
/// </summary>
public class FrontierTests
{
    private const int Ticks = 100_000;
    private const int Window = 60_000;
    private const int Stride = 32;

    private static (float level0Meta, float level1Meta) RunNested(int seed)
    {
        var stream = new NestedRegimeStream(2, seed);
        stream.Reset();
        var level0 = new Unit(new LearnedPredictiveRule(2, stateWidth: 4, drive: EncoderDrive.Variance, history: 16, quadraticFeatures: 64, seed: seed));
        var pool = new TemporalPool(Stride, 4);
        var level1 = new Unit(new LearnedPredictiveRule(4, stateWidth: 4, drive: EncoderDrive.Variance, history: 16, quadraticFeatures: 64, seed: seed));
        var level1State = new float[4];

        var l0 = new List<float[]>();
        var l1 = new List<float[]>();
        var metas = new List<float>();

        for (var t = 0; t < Ticks; t++)
        {
            var meta = stream.MetaRegime;
            var s0 = level0.Observe(stream.Next()).State;
            var pooled = pool.Push(s0);
            if (pooled != null) level1State = level1.Observe(pooled).State;
            if (t < Ticks - Window) continue;
            l0.Add(s0);
            l1.Add((float[])level1State.Clone());
            metas.Add(meta);
        }

        return (MaxAbsCorrelation(l0, metas), MaxAbsCorrelation(l1, metas));
    }

    [Fact]
    public void Level0_is_blind_to_the_meta_regime()
    {
        // The robust half: a short-window unit provably cannot see the switching-rate latent, no
        // matter the seed. This is what makes any level-1 recovery meaningful rather than a leak.
        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            var (level0Meta, _) = RunNested(seed);
            Assert.True(level0Meta < 0.15f, $"seed {seed}: level 0 should be blind to meta, got {level0Meta:F3}");
        }
    }

    [Fact]
    public void A_slowed_level1_can_recover_what_level0_cannot()
    {
        // The fragile half: on a known-good seed, the slower clock lets level 1 recover the
        // meta-regime that level 0 is blind to. This documents that it's POSSIBLE. Finding 007
        // records that it does NOT hold on every seed — that's the open problem, not a bug.
        var (level0Meta, level1Meta) = RunNested(1);
        Assert.True(level1Meta > 0.25f, $"level 1 should recover meta on seed 1, got {level1Meta:F3}");
        Assert.True(level1Meta > level0Meta + 0.2f, $"level 1 ({level1Meta:F3}) should clearly beat level 0 ({level0Meta:F3})");
    }

    [Fact]
    public void The_robust_two_level_hierarchy_owns_both_timescales_on_every_seed()
    {
        // Finding 009 — fragility solved (chose path A). A learned level 0 plus a fixed slow level
        // (TemporalLevel: change-sensing + integration) makes each level robustly own its timescale
        // on EVERY seed: level 0 the fast regime, level 1 the slow meta-regime. Contrast
        // A_slowed_level1_can_recover_what_level0_cannot, which is the same claim but seed-fragile
        // with a learned level 1.
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var stream = new NestedRegimeStream(2, seed);
            stream.Reset();
            var level0 = new Unit(new LearnedPredictiveRule(2, stateWidth: 4, drive: EncoderDrive.Variance, history: 16, quadraticFeatures: 64, seed: seed));
            var level1 = new TemporalLevel(inputWidth: 4, stride: 16, integratorRate: 0.05f);

            var l0 = new List<float[]>();
            var l1 = new List<float[]>();
            var regimes = new List<float>();
            var metas = new List<float>();

            for (var t = 0; t < Ticks; t++)
            {
                var regime = stream.Regime;
                var meta = stream.MetaRegime;
                var s0 = level0.Observe(stream.Next()).State;
                var s1 = level1.Observe(s0);
                if (t < Ticks - Window) continue;
                l0.Add(s0);
                l1.Add(s1);
                regimes.Add(regime);
                metas.Add(meta);
            }

            Assert.True(MaxAbsCorrelation(l0, regimes) > 0.6f, $"seed {seed}: level 0 should own the regime");
            Assert.True(MaxAbsCorrelation(l1, metas) > 0.35f, $"seed {seed}: level 1 should own the meta-regime");
            Assert.True(MaxAbsCorrelation(l0, metas) < 0.15f, $"seed {seed}: level 0 should stay blind to meta");
        }
    }

    [Fact]
    public void Change_sensing_plus_integration_robustly_recovers_the_meta_regime()
    {
        // Finding 008's robust path. The learned max-variance encoder gets ~0 on the meta-regime
        // even when handed a signal that correlates 0.5 with it — it discards slow structure. The
        // two temporal primitives it lacks recover it on EVERY seed: sense how much the lower level
        // changes within a window (change-energy pooling, since Mean pooling erases it), then
        // integrate that slowly (to average out per-window noise and expose the slow latent).
        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            var stream = new NestedRegimeStream(2, seed);
            stream.Reset();
            var level0 = new Unit(new LearnedPredictiveRule(2, stateWidth: 4, drive: EncoderDrive.Variance, history: 16, quadraticFeatures: 64, seed: seed));
            var pool = new TemporalPool(16, 4, PoolMode.ChangeEnergy);
            var integrator = new LeakyIntegrator(4, rate: 0.05f);

            var detector = new List<float[]>();
            var metas = new List<float>();
            var current = new float[4];

            for (var t = 0; t < Ticks; t++)
            {
                var meta = stream.MetaRegime;
                var s0 = level0.Observe(stream.Next()).State;
                var pooled = pool.Push(s0);
                if (pooled != null) current = integrator.Push(pooled);
                if (t < Ticks - Window) continue;
                detector.Add((float[])current.Clone());
                metas.Add(meta);
            }

            var corr = MaxAbsCorrelation(detector, metas);
            Assert.True(corr > 0.3f, $"seed {seed}: change+integration should recover meta, got {corr:F3}");
        }
    }

    // Max over state dimensions of |Pearson correlation| with the target.
    private static float MaxAbsCorrelation(List<float[]> states, List<float> target)
    {
        if (states.Count == 0) return 0f;
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
