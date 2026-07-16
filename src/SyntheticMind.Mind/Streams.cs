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
