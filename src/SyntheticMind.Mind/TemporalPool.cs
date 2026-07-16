namespace SyntheticMind.Mind;

/// <summary>
/// A slower clock for a higher level. It accumulates the lower level's state for `stride` ticks,
/// then emits the average and starts over. Feed a level's output through this and the next level
/// up only wakes once per window — so it necessarily sees a longer stretch of time than the level
/// below it.
///
/// This is the ingredient finding 006 said was missing: until now every level ran at the same
/// tick rate, so a higher level never saw anything a lower one didn't. A higher level can only
/// discover slower structure if it is actually looking over a slower timescale. This makes it so.
///
/// Averaging (rather than sampling every Nth state) is deliberate: it keeps information from the
/// ticks in between instead of throwing them away, which is what lets the pooled signal still
/// carry a trace of what happened across the whole window.
/// </summary>
public sealed class TemporalPool(int stride, int width)
{
    private readonly float[] _sum = new float[width];
    private int _count;

    public int Stride { get; } = stride > 0 ? stride : throw new ArgumentOutOfRangeException(nameof(stride));

    /// <summary>
    /// Add one lower-level state. Returns the pooled (averaged) window when `stride` states have
    /// arrived — that's the higher level's cue to wake and observe — otherwise null.
    /// </summary>
    public float[]? Push(float[] state)
    {
        if (state.Length != width)
            throw new ArgumentException($"Expected {width}-wide state, got {state.Length}.", nameof(state));

        for (var i = 0; i < width; i++) _sum[i] += state[i];
        _count++;

        if (_count < Stride) return null;

        var pooled = new float[width];
        for (var i = 0; i < width; i++) pooled[i] = _sum[i] / Stride;

        Array.Clear(_sum);
        _count = 0;
        return pooled;
    }
}
