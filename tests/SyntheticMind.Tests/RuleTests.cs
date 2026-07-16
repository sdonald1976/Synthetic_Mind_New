using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Behavioural tests, run through the real Experiment harness. These pin the claims finding 003
/// rests on, so that a future change that quietly breaks them fails here instead of in a
/// mysterious lab run.
/// </summary>
public class RuleTests
{
    private const int Width = 4;
    private const int Ticks = 20_000;

    [Fact]
    public void Nothing_beats_chance_on_noise()
    {
        // The negative control (SCAFFOLD.md §8). If any rule "learns" on white noise, we're
        // leaking the future into the past somewhere. A learning ratio near 1 means "did not
        // improve", which is correct here.
        foreach (IUnitRule rule in new IUnitRule[]
                 {
                     new LinearDeltaRule(Width),
                     new LearnedPredictiveRule(Width, stateWidth: 4),
                 })
        {
            var result = Experiment.Run(rule, new NoiseStream(Width), Ticks);
            Assert.True(result.LearningRatio > 0.9f,
                $"{rule.Name} appeared to learn noise (ratio {result.LearningRatio:F3}) — future leak?");
        }
    }

    [Fact]
    public void Local_delta_rule_learns_the_bouncing_ball()
    {
        var result = Experiment.Run(new LinearDeltaRule(Width), new BouncingBallStream(Width), Ticks);
        Assert.True(result.LearningRatio < 0.5f, $"expected clear learning, ratio was {result.LearningRatio:F3}");
    }

    [Fact]
    public void Variance_driven_learned_state_learns_without_collapsing()
    {
        // The v1 claim, both halves. Drive the encoder with variance and it stays informative
        // AND predicts.
        var rule = new LearnedPredictiveRule(Width, stateWidth: 4, drive: EncoderDrive.Variance);
        var result = Experiment.Run(rule, new BouncingBallStream(Width), Ticks);

        Assert.False(result.Collapse.Collapsed, $"variance-driven encoder collapsed: {result.Collapse}");
        Assert.True(result.Collapse.ParticipationRatio > 1.5f, $"rank too low: {result.Collapse}");
        Assert.True(result.LearningRatio < 0.5f, $"did not learn: ratio {result.LearningRatio:F3}");
    }

    [Fact]
    public void Predictability_driven_learned_state_collapses()
    {
        // The trapdoor, demonstrated. Same machine, encoder chases its own predictability instead
        // of variance, and the state caves in. This is the whole reason decision 6 exists.
        var rule = new LearnedPredictiveRule(Width, stateWidth: 4, drive: EncoderDrive.Predictability);
        var result = Experiment.Run(rule, new BouncingBallStream(Width), Ticks);

        Assert.True(result.Collapse.Collapsed, $"expected collapse, got {result.Collapse}");
    }

    [Fact]
    public void The_collapsed_fixture_is_caught_despite_low_error_on_constant()
    {
        // On a constant stream the collapsed fixture scores near-zero prediction error while
        // carrying zero information. Error says "perfect"; the monitor says "collapsed". The
        // monitor is right. This is the single fact the whole methodology depends on.
        var result = Experiment.Run(new CollapsedRule(Width), new ConstantStream(Width), Ticks);

        Assert.True(result.LateError < 1e-6f, "fixture should predict a constant near-perfectly");
        Assert.True(result.Collapse.Collapsed, "…and still be flagged as collapsed");
    }
}
