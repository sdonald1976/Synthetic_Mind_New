using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Pins finding 005: a nonlinear unit recovers a hidden slow cause that a linear one is blind to,
/// and the slowness objective doesn't collapse. These are the load-bearing claims; if a future
/// change breaks them, it should fail here, not silently in a lab run.
/// </summary>
public class RegimeTests
{
    private const int Ticks = 20_000;
    private const int Window = 4_000;

    // Max over state dimensions of |Pearson correlation| with the hidden regime.
    private static float RegimeCorrelation(LearnedPredictiveRule rule, float slowFreq = 0.5f, float fastFreq = 1.2f, int streamSeed = 1)
    {
        var stream = new RegimeOscillatorStream(2, streamSeed, slowFreq, fastFreq);
        stream.Reset();
        var unit = new Unit(rule);
        var states = new List<float[]>();
        var regimes = new List<float>();

        for (var t = 0; t < Ticks; t++)
        {
            var regime = stream.Regime;
            var tick = unit.Observe(stream.Next());
            if (t < Ticks - Window) continue;
            states.Add(tick.State);
            regimes.Add(regime);
        }

        var width = states[0].Length;
        var best = 0f;
        for (var d = 0; d < width; d++)
        {
            double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
            var n = states.Count;
            for (var i = 0; i < n; i++)
            {
                double x = states[i][d], y = regimes[i];
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

    [Fact]
    public void A_linear_unit_is_blind_to_the_hidden_regime()
    {
        // No product features: the frequency lives only in relationships across time, which a
        // linear map cannot form. This is the "before".
        var linear = new LearnedPredictiveRule(2, stateWidth: 4, drive: EncoderDrive.Variance, history: 16, quadraticFeatures: 0);
        Assert.True(RegimeCorrelation(linear) < 0.25f, "a linear unit should not recover the hidden regime");
    }

    [Fact]
    public void A_nonlinear_unit_recovers_the_hidden_regime()
    {
        // Same unit, plus nonlinear product features. This is the "after" — and the whole point.
        var nonlinear = new LearnedPredictiveRule(2, stateWidth: 4, drive: EncoderDrive.Variance, history: 16, quadraticFeatures: 64);
        Assert.True(RegimeCorrelation(nonlinear) > 0.5f, "a nonlinear unit should recover the hidden regime");
    }

    [Fact]
    public void The_negative_control_finds_nothing()
    {
        // Both regimes at the SAME frequency: the label still switches, but the observation is
        // identical, so there is genuinely nothing to detect. If the unit still "finds" the regime,
        // the recovery elsewhere is a leak or an artifact of slow-drift, not real detection. It
        // must score chance. This is the control that makes the positive result trustworthy.
        var unit = new LearnedPredictiveRule(2, stateWidth: 4, drive: EncoderDrive.Variance, history: 16, quadraticFeatures: 64);
        Assert.True(RegimeCorrelation(unit, slowFreq: 0.8f, fastFreq: 0.8f) < 0.25f,
            "with nothing to detect, correlation must fall to chance");
    }

    [Fact]
    public void Recovery_holds_on_a_different_seed()
    {
        // Guards against a lucky-seed result: a fresh stream seed and a fresh set of random
        // product features must still recover the regime.
        var unit = new LearnedPredictiveRule(2, stateWidth: 4, drive: EncoderDrive.Variance, history: 16, quadraticFeatures: 64, seed: 5);
        Assert.True(RegimeCorrelation(unit, streamSeed: 5) > 0.5f, "recovery should not depend on a particular seed");
    }

    [Fact]
    public void The_slowness_objective_stays_informative()
    {
        // NOTE: CollapseMonitor's participation-ratio test does NOT apply to a slowness encoder.
        // Slowness deliberately seeks low-variance (slow) features, so some state dimensions end up
        // near-constant — which reads as "collapsed" to a variance-based detector even though the
        // top slow feature is the single most informative thing in the experiment. The anti-collapse
        // instrument is objective-specific. So the honest health check for slowness is whether its
        // state carries real information, measured directly: does it recover the hidden regime?
        var slow = new LearnedPredictiveRule(2, stateWidth: 4, drive: EncoderDrive.Slowness, history: 16, quadraticFeatures: 64);
        Assert.True(RegimeCorrelation(slow) > 0.5f, "slow-feature encoder should recover the hidden regime");
    }
}
