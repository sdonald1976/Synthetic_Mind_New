namespace SyntheticMind.Mind;

/// <summary>How a <see cref="TemporalPool"/> summarizes a window of lower-level states.</summary>
public enum PoolMode
{
    /// <summary>The window average. Preserves WHAT the lower level represented, on average.</summary>
    Mean,

    /// <summary>
    /// How much the lower level's state CHANGED across the window: the mean squared step between
    /// consecutive states. This is the change-sensing primitive from finding 008 — and the finding's
    /// hard-won point is that Mean pooling DESTROYS it. A latent defined by a rate (how often
    /// something switches) leaves its trace in the within-window motion, which averaging erases.
    /// </summary>
    ChangeEnergy,
}

/// <summary>
/// A slower clock for a higher level. It accumulates the lower level's state for `stride` ticks,
/// then emits a summary of that window and starts over. Feed a level's output through this and the
/// next level up only wakes once per window — so it necessarily sees a longer stretch of time than
/// the level below it.
///
/// This is the ingredient finding 006 said was missing: until now every level ran at the same tick
/// rate, so a higher level never saw anything a lower one didn't. A higher level can only discover
/// slower structure if it is actually looking over a slower timescale. This makes it so.
///
/// The summary is chosen by <see cref="PoolMode"/>. Finding 008: which summary you take is not a
/// detail — <see cref="PoolMode.Mean"/> preserves slow *level* structure, <see cref="PoolMode.ChangeEnergy"/>
/// preserves slow *rate* structure, and each is blind to the other's latent.
/// </summary>
public sealed class TemporalPool(int stride, int width, PoolMode mode = PoolMode.Mean)
{
    private readonly float[] _sum = new float[width];
    private readonly float[] _previous = new float[width];
    private bool _havePrevious;
    private int _count;

    public int Stride { get; } = stride > 0 ? stride : throw new ArgumentOutOfRangeException(nameof(stride));
    public PoolMode Mode => mode;

    /// <summary>
    /// Add one lower-level state. Returns the window summary when `stride` states have arrived —
    /// the higher level's cue to wake and observe — otherwise null.
    /// </summary>
    public float[]? Push(float[] state)
    {
        if (state.Length != width)
            throw new ArgumentException($"Expected {width}-wide state, got {state.Length}.", nameof(state));

        switch (mode)
        {
            case PoolMode.Mean:
                for (var i = 0; i < width; i++) _sum[i] += state[i];
                break;

            case PoolMode.ChangeEnergy:
                if (_havePrevious)
                    for (var i = 0; i < width; i++)
                    {
                        var step = state[i] - _previous[i];
                        _sum[i] += step * step;
                    }
                Array.Copy(state, _previous, width);
                _havePrevious = true;
                break;
        }
        _count++;

        if (_count < Stride) return null;

        var pooled = new float[width];
        for (var i = 0; i < width; i++) pooled[i] = _sum[i] / Stride;

        Array.Clear(_sum);
        _count = 0;
        return pooled;
    }
}
