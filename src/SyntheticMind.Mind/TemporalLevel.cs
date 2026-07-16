namespace SyntheticMind.Mind;

/// <summary>
/// A higher level built from fixed temporal primitives instead of a learned max-variance encoder —
/// the path chosen after finding 008, which showed the learned encoder is structurally blind to
/// slow structure (it turned a 0.5-correlated signal into 0.00). Rather than hope a learning rule
/// discovers slowness, this level *has* the machinery for it, always on, and learning happens on
/// top (a downstream readout or a further level).
///
/// It watches a faster level's state stream and, once per <c>stride</c> window, produces a slow
/// summary that combines two things — because in general you don't know which one carries the slow
/// latent:
///   - integrated MEAN pool — slow *level* structure ("what is the level below settling at?")
///   - integrated CHANGE-ENERGY pool — slow *rate* structure ("how fast is it churning?")
///
/// Each is a fixed pool (finding 006's slower clock) followed by a leaky integrator (finding 008's
/// smoothing), and the two are concatenated. State width is therefore twice the input width.
///
/// This is deliberately NOT learned. It's the "built-in sense" — like the retina's fixed
/// pre-processing (SCAFFOLD.md §4), one rung up: fixed temporal machinery that hands a clean, slow
/// representation to whatever learns above it.
/// </summary>
public sealed class TemporalLevel
{
    private readonly TemporalPool _meanPool;
    private readonly TemporalPool _changePool;
    private readonly LeakyIntegrator _meanIntegrator;
    private readonly LeakyIntegrator _changeIntegrator;
    private readonly int _inputWidth;
    private float[] _state;

    /// <param name="stride">Window length — how much slower this level's clock runs.</param>
    /// <param name="integratorRate">Leak of the smoothing stage; smaller = longer, steadier memory.</param>
    public TemporalLevel(int inputWidth, int stride, float integratorRate = 0.05f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputWidth);

        _inputWidth = inputWidth;
        _meanPool = new TemporalPool(stride, inputWidth, PoolMode.Mean);
        _changePool = new TemporalPool(stride, inputWidth, PoolMode.ChangeEnergy);
        _meanIntegrator = new LeakyIntegrator(inputWidth, integratorRate);
        _changeIntegrator = new LeakyIntegrator(inputWidth, integratorRate);
        _state = new float[2 * inputWidth];
    }

    /// <summary>Width of the summary this level publishes: [integrated mean | integrated change].</summary>
    public int StateWidth => 2 * _inputWidth;

    /// <summary>The current slow summary. It only refreshes once per window; between windows it
    /// holds steady, which is exactly the point — a higher level should change slowly.</summary>
    public float[] State => (float[])_state.Clone();

    /// <summary>Feed one state from the level below. Returns the current slow summary every tick
    /// (refreshed on window boundaries), so callers don't have to track the clock themselves.</summary>
    public float[] Observe(float[] lowerState)
    {
        var mean = _meanPool.Push(lowerState);
        var change = _changePool.Push(lowerState);

        if (mean is not null && change is not null)
        {
            var m = _meanIntegrator.Push(mean);
            var c = _changeIntegrator.Push(change);
            Array.Copy(m, 0, _state, 0, _inputWidth);
            Array.Copy(c, 0, _state, _inputWidth, _inputWidth);
        }

        return State;
    }
}
