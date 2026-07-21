namespace SyntheticMind.Vision;

/// <summary>
/// The eye's fixed front-end: a frame in, a feature vector out. This is to video what the cochlea
/// is to audio (SCAFFOLD.md §4) — dumb, fixed, learns nothing. A real retina doesn't ship raw
/// pixels to the brain; it compresses into coarse brightness, MOTION, and — the sharp part — the
/// oriented EDGES that V1 simple cells detect, which is what carries shape (finding 025).
///
/// Per frame, over a downsampled grid of cells:
///   - BRIGHTNESS — what's light and dark, roughly where
///   - MOTION — how much each cell changed since last frame (optional)
///   - COLOR — the mean red/green/blue of each cell (optional). Grayscale throws away the single
///     biggest cue in colourful content: pink shirt vs. green backdrop vs. red Elmo are identical
///     in brightness but obvious in colour (finding 032).
///   - ORIENTED EDGES — a small histogram of gradient orientations per cell (optional): where the
///     edges are and which way they run. Two objects that share brightness but differ in shape
///     look the same to brightness alone and different here.
/// </summary>
public sealed class Retina
{
    private readonly int _grid;
    private readonly bool _motion;
    private readonly bool _color;
    private readonly int _orientations;
    private float[]? _previousBrightness;

    public Retina(int grid = 8, bool motion = true, int orientations = 0, bool color = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(orientations);
        _grid = grid;
        _motion = motion;
        _color = color;
        _orientations = orientations;
    }

    /// <summary>Feature width: brightness + optional motion + optional colour (3) + optional oriented-edge bins, all per cell.</summary>
    public int Width => _grid * _grid * (1 + (_motion ? 1 : 0) + (_color ? 3 : 0) + _orientations);

    /// <summary>Whether this retina expects red/green/blue planes passed to <see cref="Process"/>.</summary>
    public bool UsesColor => _color;

    /// <param name="pixels">Grayscale/luminance plane — brightness, motion and edges are read from this.</param>
    /// <param name="red">Red, green, blue planes (same size as <paramref name="pixels"/>), required when
    /// colour is enabled; ignored otherwise.</param>
    public float[] Process(float[] pixels, int width, int height, float[]? red = null, float[]? green = null, float[]? blue = null)
    {
        if (pixels.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", nameof(pixels));
        if (_color && (red is null || green is null || blue is null))
            throw new ArgumentException("Colour retina needs red/green/blue planes.", nameof(red));

        var cells = _grid * _grid;
        var brightness = MeanGrid(pixels, width, height);

        var output = new float[Width];
        Array.Copy(brightness, 0, output, 0, cells);
        var offset = cells;

        if (_motion)
        {
            if (_previousBrightness is not null)
                for (var i = 0; i < cells; i++) output[offset + i] = MathF.Abs(brightness[i] - _previousBrightness[i]);
            _previousBrightness = brightness;
            offset += cells;
        }

        if (_color)
        {
            foreach (var plane in new[] { red!, green!, blue! })
            {
                Array.Copy(MeanGrid(plane, width, height), 0, output, offset, cells);
                offset += cells;
            }
        }

        if (_orientations > 0)
            AccumulateOrientedEdges(pixels, width, height, output, offset);

        return output;
    }

    public void Reset() => _previousBrightness = null;

    private float[] MeanGrid(float[] plane, int width, int height)
    {
        var grid = new float[_grid * _grid];
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
                    for (var x = x0; x < x1; x++) { sum += plane[y * width + x]; n++; }
                grid[gy * _grid + gx] = sum / n;
            }
        }
        return grid;
    }

    /// <summary>
    /// HOG-style: for every interior pixel, take the gradient, bin its (unsigned) orientation, and
    /// add its magnitude to that bin of the cell it falls in. A vertical edge → a horizontal
    /// gradient → the first bin; a horizontal edge → the middle bin. Per-cell energy is normalized
    /// by cell area so a bigger cell isn't automatically "more edge".
    /// </summary>
    private void AccumulateOrientedEdges(float[] pixels, int width, int height, float[] output, int offset)
    {
        var perCell = width / (float)_grid * (height / (float)_grid);
        for (var y = 1; y < height - 1; y++)
        {
            var gyCell = Math.Min(_grid - 1, y * _grid / height);
            for (var x = 1; x < width - 1; x++)
            {
                var gx = pixels[y * width + x + 1] - pixels[y * width + x - 1];
                var gyGrad = pixels[(y + 1) * width + x] - pixels[(y - 1) * width + x];
                var mag = MathF.Sqrt(gx * gx + gyGrad * gyGrad);
                if (mag < 1e-4f) continue;

                var angle = MathF.Atan2(gyGrad, gx);      // [-π, π]
                if (angle < 0) angle += MathF.PI;          // unsigned orientation [0, π)
                var bin = Math.Min(_orientations - 1, (int)(angle / MathF.PI * _orientations));

                var gxCell = Math.Min(_grid - 1, x * _grid / width);
                var cell = gyCell * _grid + gxCell;
                output[offset + cell * _orientations + bin] += mag / perCell;
            }
        }
    }
}
