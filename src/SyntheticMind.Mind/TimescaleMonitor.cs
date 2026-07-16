namespace SyntheticMind.Mind;

/// <summary>How fast a unit's state changes from one tick to the next.</summary>
/// <param name="Persistence">
/// Lag-1 autocorrelation, roughly. 0 means "every tick is unrelated to the last" (fast, twitchy).
/// 1 means "barely moves tick to tick" (slow, sluggish). This is the number that says whether a
/// level represents something slow.
/// </param>
public readonly record struct TimescaleReport(float Persistence)
{
    /// <summary>
    /// A rough "how many ticks until the state substantially changes." Persistence 0.9 → ~10 ticks.
    /// Only meaningful when the state isn't collapsed — a constant state is trivially persistent
    /// and means nothing, so always read this next to a CollapseReport.
    /// </summary>
    public float CharacteristicTicks => Persistence >= 0.999f ? float.PositiveInfinity
        : Persistence <= 0f ? 1f
        : 1f / (1f - Persistence);

    public override string ToString() =>
        $"persistence {Persistence:F3} (~{CharacteristicTicks:F0} ticks)";
}

/// <summary>
/// Measures the timescale of a unit's state: does it change every tick, or hold steady for a
/// while? This is the instrument the abstraction thesis (SCAFFOLD.md §3) has been missing.
///
/// The claim to be tested is "a level further from the input represents slower structure." That
/// is exactly a claim about persistence — so we need to measure persistence to test it. Rank
/// (CollapseMonitor) says the state is informative; persistence says how fast it moves. Emergent
/// abstraction is the conjunction: a stacked level that is BOTH still informative AND slower than
/// the level below it. Slow-but-collapsed is not abstraction, it's just death.
///
/// The measure: for a signal with variance V, the expected squared step between consecutive
/// samples is E[‖s_t − s_{t-1}‖²] = 2·V·(1 − r), where r is the lag-1 autocorrelation. Invert
/// that for r. No model, no eigensolver — just two running sums.
/// </summary>
public sealed class TimescaleMonitor(int width)
{
    private readonly List<float[]> _samples = [];

    public int Count => _samples.Count;

    public void Record(float[] state)
    {
        if (state.Length != width)
            throw new ArgumentException($"Expected {width}-wide state, got {state.Length}.", nameof(state));
        _samples.Add((float[])state.Clone());
    }

    public TimescaleReport Report()
    {
        if (_samples.Count < 2) return new TimescaleReport(0f);

        var n = _samples.Count;

        var mean = new double[width];
        foreach (var s in _samples)
            for (var i = 0; i < width; i++) mean[i] += s[i];
        for (var i = 0; i < width; i++) mean[i] /= n;

        // spread = mean squared distance of each sample from the overall mean (total variability).
        var spread = 0.0;
        foreach (var s in _samples)
            for (var i = 0; i < width; i++)
            {
                var d = s[i] - mean[i];
                spread += d * d;
            }
        spread /= n;

        // step = mean squared distance between consecutive samples (how far it hops per tick).
        var step = 0.0;
        for (var t = 1; t < n; t++)
            for (var i = 0; i < width; i++)
            {
                var d = _samples[t][i] - _samples[t - 1][i];
                step += d * d;
            }
        step /= n - 1;

        if (spread < 1e-30) return new TimescaleReport(0f);

        // r = 1 − step/(2·spread). Clamp: numerical noise and non-stationarity can push it a hair
        // outside [0,1], which would just be confusing to read.
        var persistence = 1.0 - step / (2.0 * spread);
        return new TimescaleReport((float)Math.Clamp(persistence, 0.0, 1.0));
    }

    public void Clear() => _samples.Clear();
}
