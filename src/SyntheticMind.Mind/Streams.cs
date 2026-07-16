namespace SyntheticMind.Mind;

/// <summary>
/// A stream that never changes. Trivially predictable.
///
/// This is the control that catches self-deception: a COLLAPSED model and a PERFECT model
/// produce identical loss curves here. If our metrics can't tell those apart on this stream,
/// they can't tell them apart anywhere, and every later number is worthless.
/// </summary>
public sealed class ConstantStream(int width = 8) : IStream
{
    public string Name => "constant";
    public int Width => width;
    public float[] Next() => Enumerable.Repeat(0.5f, width).ToArray();
    public void Reset() { }
}

/// <summary>
/// White noise. Structurally unpredictable — there is nothing here to learn.
///
/// The negative control. Any model that appears to beat chance on this stream is broken,
/// or we're leaking the future into the past somewhere. Expect to fail this one; be alarmed
/// if we don't.
/// </summary>
public sealed class NoiseStream(int width = 8, int seed = 1) : IStream
{
    private Random _random = new(seed);

    public string Name => "noise";
    public int Width => width;

    public float[] Next()
    {
        var v = new float[width];
        for (var i = 0; i < width; i++) v[i] = (float)_random.NextDouble();
        return v;
    }

    public void Reset() => _random = new Random(seed);
}

/// <summary>
/// A ball bouncing in a 2-D box, rendered as [x, y, ...padding].
///
/// Chosen because it has structure at TWO timescales, which is the whole hierarchy thesis
/// (SCAFFOLD.md §3):
///   - fast: position moves smoothly and predictably, tick to tick
///   - slow: velocity flips sign at the walls, on a much longer period
///
/// A single unit should learn the fast part. If a SECOND unit stacked above it discovers the
/// slow part — the bounce rhythm — without being told to, that's the thesis demonstrated in
/// one experiment. If it doesn't, the bet is wrong and we learn it cheaply, here, rather than
/// after building a perception stack.
/// </summary>
public sealed class BouncingBallStream : IStream
{
    private const float SpeedX = 0.031f;
    private const float SpeedY = 0.017f;   // deliberately incommensurate with SpeedX, so the two
                                           // axes don't share a bounce period and the slow
                                           // structure isn't trivially periodic

    private readonly int _width;
    private readonly int _seed;
    private float _x, _y, _vx, _vy;

    public BouncingBallStream(int width = 8, int seed = 1)
    {
        _width = width;
        _seed = seed;
        Reset();
    }

    public string Name => "bouncing-ball";
    public int Width => _width;

    public float[] Next()
    {
        _x += _vx;
        _y += _vy;
        if (_x is < 0f or > 1f) { _vx = -_vx; _x = Math.Clamp(_x, 0f, 1f); }
        if (_y is < 0f or > 1f) { _vy = -_vy; _y = Math.Clamp(_y, 0f, 1f); }

        var v = new float[_width];
        v[0] = _x;
        if (_width > 1) v[1] = _y;
        // Remaining channels stay zero: dead dimensions the model must learn to ignore.
        // Real sensors have plenty of these.
        return v;
    }

    public void Reset()
    {
        var random = new Random(_seed);
        _x = (float)random.NextDouble();
        _y = (float)random.NextDouble();
        _vx = SpeedX;
        _vy = SpeedY;
    }
}

/// <summary>
/// A fast oscillation whose FREQUENCY is set by a slow hidden regime that flips every few hundred
/// ticks. This is the stream the bouncing ball wasn't: it has genuine two-timescale structure with
/// a slow *latent cause*, which is the only setting where "does a higher level discover something
/// slower?" is even a fair question (finding 005).
///
/// What you observe: sin(phase) and cos(phase) — a fast wiggle, changing every tick.
/// What's hidden: the regime (0 or 1), which sets the wiggle's speed and changes rarely.
///
/// The regime is NOT in the observation. Inferring it requires watching the wiggle over time —
/// you cannot read a frequency from a single instant. So a level that comes to represent the
/// regime has genuinely abstracted a slow hidden cause it was never shown. <see cref="Regime"/>
/// exposes the ground truth for MEASUREMENT ONLY; it is never fed to the model.
/// </summary>
public sealed class RegimeOscillatorStream : IStream
{
    private const float SlowFreq = 0.50f;   // radians/tick in regime 0 (~12-tick cycle)
    private const float FastFreq = 1.20f;   // radians/tick in regime 1 (~5-tick cycle)
    private const int MinDwell = 150;       // regimes last this long, at least — the slow timescale
    private const int MaxDwell = 350;

    private readonly int _width;
    private readonly int _seed;
    private Random _random = null!;
    private float _phase;
    private int _regime;
    private int _ticksLeft;

    public RegimeOscillatorStream(int width = 4, int seed = 1)
    {
        _width = width;
        _seed = seed;
        Reset();
    }

    public string Name => "regime-oscillator";
    public int Width => _width;

    /// <summary>The hidden slow latent, 0 or 1. For measuring abstraction — never an input.</summary>
    public float Regime => _regime;

    public float[] Next()
    {
        if (_ticksLeft <= 0)
        {
            _regime = 1 - _regime;
            _ticksLeft = _random.Next(MinDwell, MaxDwell);
        }
        _ticksLeft--;

        _phase += _regime == 0 ? SlowFreq : FastFreq;

        var v = new float[_width];
        v[0] = MathF.Sin(_phase);
        if (_width > 1) v[1] = MathF.Cos(_phase);
        // channels 2+ stay zero: distractor dimensions, as ever
        return v;
    }

    public void Reset()
    {
        _random = new Random(_seed);
        _phase = 0f;
        _regime = 0;
        _ticksLeft = _random.Next(MinDwell, MaxDwell);
    }
}
