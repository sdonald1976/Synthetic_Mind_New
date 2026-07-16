namespace SyntheticMind.Mind;

/// <param name="EarlyError">Mean squared error over the first window — before it learned anything.</param>
/// <param name="LateError">Mean squared error over the final window — what it settled at.</param>
public readonly record struct RunResult(
    string Rule,
    string Stream,
    int Ticks,
    float EarlyError,
    float LateError,
    CollapseReport Collapse)
{
    /// <summary>How much it improved. &lt;1 is learning; ~1 is not.</summary>
    public float LearningRatio => EarlyError < 1e-12f ? 1f : LateError / EarlyError;
}

/// <summary>
/// Runs one rule against one stream and reports both numbers that matter: how well it predicted,
/// and whether its state still means anything (SCAFFOLD.md §8 — the exit criteria are BOTH, not
/// either).
/// </summary>
public static class Experiment
{
    public static RunResult Run(IUnitRule rule, IStream stream, int ticks = 20_000, int window = 1_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, window * 2);

        stream.Reset();
        var unit = new Unit(rule);
        var monitor = new CollapseMonitor(rule.StateWidth);

        var earlyError = 0f;
        var lateError = 0f;

        for (var t = 0; t < ticks; t++)
        {
            var tick = unit.Observe(stream.Next());

            if (t < window) earlyError += tick.SquaredError;
            if (t >= ticks - window)
            {
                lateError += tick.SquaredError;
                // Only sample state once it's settled. Sampling the transient would credit the
                // model with variance that's really just it thrashing on its way to a solution —
                // which would hide a collapse that happens later.
                monitor.Record(tick.State);
            }
        }

        return new RunResult(
            rule.Name, stream.Name, ticks,
            earlyError / window, lateError / window,
            monitor.Report());
    }
}
