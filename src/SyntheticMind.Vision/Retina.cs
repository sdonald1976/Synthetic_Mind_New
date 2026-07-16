namespace SyntheticMind.Vision;

/// <summary>
/// The eye's fixed front-end: a frame in, a feature vector out. This is to video what the cochlea
/// is to audio (SCAFFOLD.md §4) — dumb, fixed, learns nothing. A real retina doesn't ship raw
/// pixels to the brain either; it compresses ~100:1 into coarse brightness and, crucially, MOTION.
///
/// Per frame it produces two coarse grids over a downsampled image:
///   - BRIGHTNESS — what's light and dark, and roughly where ("what's in front of me")
///   - MOTION — how much each cell changed since the last frame ("what's moving")
///
/// Motion matters enormously for video: a still scene barely changes, but anything that moves
/// lights up — which is exactly the kind of structure a predictive hierarchy can grab onto.
/// </summary>
public sealed class Retina
{
    private readonly int _grid;
    private readonly bool _motion;
    private float[]? _previous;

    public Retina(int grid = 8, bool motion = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(grid);
        _grid = grid;
        _motion = motion;
    }

    /// <summary>Feature width: grid² brightness cells, doubled if motion is on.</summary>
    public int Width => _grid * _grid * (_motion ? 2 : 1);

    /// <summary>
    /// Downsample a grayscale frame (row-major, values 0..1) to the brightness grid, and append a
    /// motion grid (absolute change since the previous frame).
    /// </summary>
    public float[] Process(float[] pixels, int width, int height)
    {
        if (pixels.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", nameof(pixels));

        var brightness = new float[_grid * _grid];
        for (var gy = 0; gy < _grid; gy++)
        {
            var y0 = gy * height / _grid;
            var y1 = Math.Max(y0 + 1, (gy + 1) * height / _grid);
            for (var gx = 0; gx < _grid; gx++)
            {
                var x0 = gx * width / _grid;
                var x1 = Math.Max(x0 + 1, (gx + 1) * width / _grid);
                var sum = 0f; var n = 0;
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++) { sum += pixels[y * width + x]; n++; }
                brightness[gy * _grid + gx] = sum / n;
            }
        }

        if (!_motion) return brightness;

        var output = new float[_grid * _grid * 2];
        Array.Copy(brightness, 0, output, 0, brightness.Length);
        if (_previous is not null)
            for (var i = 0; i < brightness.Length; i++)
                output[brightness.Length + i] = MathF.Abs(brightness[i] - _previous[i]);
        _previous = brightness;
        return output;
    }

    public void Reset() => _previous = null;
}
