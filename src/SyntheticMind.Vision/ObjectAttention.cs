namespace SyntheticMind.Vision;

/// <summary>
/// Attention: pick the sub-region that stands out and represent THAT, not the whole frame — the first
/// step toward objects instead of scenes (finding 037). A whole-frame summary can only ever learn
/// "the outdoor scene"; to learn "the bus" you must look at the bus. This is a saliency fovea: it
/// scores every patch by how much it pops from the background (edges + colour-difference-from-the-
/// frame-average + motion), jumps to the peak, and runs a <see cref="Retina"/> on a window there.
///
/// Dumb and fixed, like the retina behind it. It won't segment a clean object outline — it crops a
/// salient window — but a distinctive coloured thing (a yellow bus, a bright card) reliably wins the
/// saliency vote, so recurring objects land on recurring windows.
/// </summary>
public sealed class ObjectAttention
{
    private readonly Retina _fovea;
    private readonly int _coarseX, _coarseY;
    private readonly float _windowFrac;
    private float[]? _prevLuma;

    /// <param name="fovea">Retina applied to the attended window (colour on, motion off — the window
    /// jumps around, so cross-frame motion inside it is meaningless).</param>
    /// <param name="windowFrac">Window side as a fraction of the frame (0.5 = half width/height).</param>
    public ObjectAttention(Retina fovea, int coarseX = 16, int coarseY = 12, float windowFrac = 0.5f)
    {
        _fovea = fovea;
        _coarseX = coarseX;
        _coarseY = coarseY;
        _windowFrac = windowFrac;
    }

    public int Width => _fovea.Width;

    /// <summary>Attend to the most salient window and return its features plus the window box.</summary>
    public (float[] Features, int X0, int Y0, int W, int H) Attend(
        float[] luma, float[] red, float[] green, float[] blue, int width, int height)
    {
        float mR = Mean(red), mG = Mean(green), mB = Mean(blue);

        // Coarse saliency grid: edges + colour-pop + motion, summed per coarse cell.
        var sal = new float[_coarseX * _coarseY];
        for (var y = 1; y < height - 1; y++)
        {
            var cy = y * _coarseY / height;
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                var edge = MathF.Abs(luma[i + 1] - luma[i - 1]) + MathF.Abs(luma[i + width] - luma[i - width]);
                var col = MathF.Abs(red[i] - mR) + MathF.Abs(green[i] - mG) + MathF.Abs(blue[i] - mB);
                var mot = _prevLuma is not null ? MathF.Abs(luma[i] - _prevLuma[i]) : 0f;
                var cx = x * _coarseX / width;
                sal[cy * _coarseX + cx] += edge + col + 0.5f * mot;
            }
        }

        var peak = 0;
        for (var c = 1; c < sal.Length; c++) if (sal[c] > sal[peak]) peak = c;
        var pcx = (peak % _coarseX + 0.5f) / _coarseX * width;
        var pcy = (peak / _coarseX + 0.5f) / _coarseY * height;

        var wW = Math.Max(4, (int)(width * _windowFrac));
        var wH = Math.Max(4, (int)(height * _windowFrac));
        var x0 = Math.Clamp((int)(pcx - wW / 2f), 0, width - wW);
        var y0 = Math.Clamp((int)(pcy - wH / 2f), 0, height - wH);

        var (cl, cr, cg, cb) = Crop(luma, red, green, blue, width, x0, y0, wW, wH);
        var feat = _fovea.Process(cl, wW, wH, cr, cg, cb);

        _prevLuma = (float[])luma.Clone();
        return (feat, x0, y0, wW, wH);
    }

    private static (float[] L, float[] R, float[] G, float[] B) Crop(
        float[] luma, float[] red, float[] green, float[] blue, int width, int x0, int y0, int wW, int wH)
    {
        float[] cl = new float[wW * wH], cr = new float[wW * wH], cg = new float[wW * wH], cb = new float[wW * wH];
        for (var y = 0; y < wH; y++)
        {
            var src = (y0 + y) * width + x0;
            var dst = y * wW;
            for (var x = 0; x < wW; x++)
            {
                cl[dst + x] = luma[src + x];
                cr[dst + x] = red[src + x];
                cg[dst + x] = green[src + x];
                cb[dst + x] = blue[src + x];
            }
        }
        return (cl, cr, cg, cb);
    }

    private static float Mean(float[] v)
    {
        var s = 0f;
        foreach (var x in v) s += x;
        return s / v.Length;
    }
}
