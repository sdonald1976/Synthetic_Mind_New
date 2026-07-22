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
    private readonly bool _novelty;
    private readonly float _bgRate;
    private float[]? _prevLuma;
    private float[]? _bgL, _bgR, _bgG, _bgB;   // running background model (novelty mode)

    /// <param name="fovea">Retina applied to the attended window (colour on, motion off — the window
    /// jumps around, so cross-frame motion inside it is meaningless).</param>
    /// <param name="windowFrac">Window side as a fraction of the frame (0.5 = half width/height).</param>
    /// <param name="novelty">Score by NOVELTY (difference from a running background) instead of raw
    /// saliency (finding 038). Saliency lands on faces — they're the most detailed, contrasty thing on
    /// screen and always present. Novelty ignores whatever is usually there (the face gets absorbed
    /// into the background) and fires on what just APPEARED (a held-up object), which is what naming
    /// actually keys on.</param>
    /// <param name="bgRate">How fast the background model absorbs the current frame. Slower = an object
    /// stays "novel" longer; too slow and a moved presenter never fades.</param>
    public ObjectAttention(Retina fovea, int coarseX = 16, int coarseY = 12, float windowFrac = 0.5f,
        bool novelty = false, float bgRate = 0.03f)
    {
        _fovea = fovea;
        _coarseX = coarseX;
        _coarseY = coarseY;
        _windowFrac = windowFrac;
        _novelty = novelty;
        _bgRate = bgRate;
    }

    public int Width => _fovea.Width;

    /// <summary>Attend to the most salient window and return its features plus the window box.</summary>
    public (float[] Features, int X0, int Y0, int W, int H) Attend(
        float[] luma, float[] red, float[] green, float[] blue, int width, int height)
    {
        float mR = Mean(red), mG = Mean(green), mB = Mean(blue);
        var sal = _novelty
            ? NoveltyGrid(luma, red, green, blue, width, height, mR, mG, mB)
            : SaliencyGrid(luma, red, green, blue, width, height, mR, mG, mB);

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

    // Raw saliency: edges + colour-pop + motion. Lands on the most detailed/contrasty region — often a face.
    private float[] SaliencyGrid(float[] luma, float[] red, float[] green, float[] blue, int width, int height, float mR, float mG, float mB)
    {
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
                sal[cy * _coarseX + x * _coarseX / width] += edge + col + 0.5f * mot;
            }
        }
        return sal;
    }

    // Novelty: how much each region differs from the running background, then absorb the frame into it.
    // Whatever is usually there (the face) fades into the background and scores ~0; what just appeared pops.
    private float[] NoveltyGrid(float[] luma, float[] red, float[] green, float[] blue, int width, int height, float mR, float mG, float mB)
    {
        var n = width * height;
        if (_bgL is null)   // seed the background with the first frame (nothing is "novel" yet)
        {
            _bgL = (float[])luma.Clone(); _bgR = (float[])red.Clone(); _bgG = (float[])green.Clone(); _bgB = (float[])blue.Clone();
        }

        var sal = new float[_coarseX * _coarseY];
        for (var y = 0; y < height; y++)
        {
            var cy = y * _coarseY / height;
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var nov = MathF.Abs(luma[i] - _bgL![i]) + MathF.Abs(red[i] - _bgR![i])
                        + MathF.Abs(green[i] - _bgG![i]) + MathF.Abs(blue[i] - _bgB![i]);
                // a small colour-pop tiebreak so, among novel regions, the more distinctive one wins
                var col = MathF.Abs(red[i] - mR) + MathF.Abs(green[i] - mG) + MathF.Abs(blue[i] - mB);
                sal[cy * _coarseX + x * _coarseX / width] += nov + 0.25f * col;

                _bgL[i] += _bgRate * (luma[i] - _bgL[i]);
                _bgR![i] += _bgRate * (red[i] - _bgR[i]);
                _bgG![i] += _bgRate * (green[i] - _bgG[i]);
                _bgB![i] += _bgRate * (blue[i] - _bgB[i]);
            }
        }
        return sal;
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
