namespace SyntheticMind.Vision;

/// <summary>How attention chooses where to look each frame.</summary>
public enum AttentionMode
{
    /// <summary>Raw saliency: edges + colour-pop + motion. Lands on the most contrasty region — usually a face.</summary>
    Saliency,
    /// <summary>Novelty: difference from a running background. Fires on what just appeared, ignores what's always there.</summary>
    Novelty,
    /// <summary>Person-centred (finding 039): the way a child actually watches a teacher. Hold on the PERSON
    /// (the persistent salient anchor); when something new appears — the person holds up or points to a thing —
    /// glance at it for a second or two, then snap back to the person. Saliency finds the anchor; novelty
    /// triggers the glance. This is joint attention: you learn the referent through the teacher, not by ignoring them.</summary>
    PersonCentred,
}

/// <summary>
/// Attention: which sub-region to represent each frame. A whole-frame summary can only learn "the scene";
/// to learn a thing you must look at the thing — but as finding 038/039 showed, you don't do that by
/// suppressing the person. A learner watches the teacher and briefly follows what the teacher shows.
/// </summary>
public sealed class ObjectAttention
{
    private readonly Retina _fovea;
    private readonly int _coarseX, _coarseY;
    private readonly float _windowFrac;
    private readonly AttentionMode _mode;
    private readonly float _bgRate;
    private readonly int _glanceFrames;      // how long a glance at a held-up thing lasts
    private readonly float _glanceTrigger;   // novelty spike (× baseline) that earns a glance

    private float[]? _prevLuma;
    private float[]? _bgL, _bgR, _bgG, _bgB;  // running background (novelty)
    private float _anchorX, _anchorY;         // smoothed person location (coarse cells)
    private bool _anchorInit;
    private float _novBaseline;
    private bool _novInit;
    private int _glanceLeft;                  // frames remaining in the current glance

    public ObjectAttention(Retina fovea, int coarseX = 16, int coarseY = 12, float windowFrac = 0.5f,
        AttentionMode mode = AttentionMode.Saliency, float bgRate = 0.03f,
        int glanceFrames = 14, float glanceTrigger = 2.5f)
    {
        _fovea = fovea;
        _coarseX = coarseX;
        _coarseY = coarseY;
        _windowFrac = windowFrac;
        _mode = mode;
        _bgRate = bgRate;
        _glanceFrames = glanceFrames;
        _glanceTrigger = glanceTrigger;
    }

    public int Width => _fovea.Width;

    /// <summary>True while attention is glancing away at a held-up/pointed-at thing (vs. resting on the person).</summary>
    public bool Glancing => _glanceLeft > 0;

    public (float[] Features, int X0, int Y0, int W, int H) Attend(
        float[] luma, float[] red, float[] green, float[] blue, int width, int height)
    {
        float mR = Mean(red), mG = Mean(green), mB = Mean(blue);
        int focus;

        if (_mode == AttentionMode.PersonCentred)
        {
            var sal = SaliencyGrid(luma, red, green, blue, width, height, mR, mG, mB);   // the person (anchor)
            var nov = NoveltyGrid(luma, red, green, blue, width, height, mR, mG, mB);     // what just appeared
            var salPeak = ArgMax(sal);
            var novPeak = ArgMax(nov);
            var novStrength = nov[novPeak];
            if (!_novInit) { _novBaseline = novStrength; _novInit = true; }
            if (!_anchorInit) { _anchorX = salPeak % _coarseX; _anchorY = salPeak / _coarseX; _anchorInit = true; }

            if (_glanceLeft > 0)
            {
                _glanceLeft--;               // keep looking at the thing it glanced to
                focus = novPeak;
            }
            else
            {
                // Resting on the person: drift the anchor toward the current salient (person) region.
                _anchorX += 0.1f * (salPeak % _coarseX - _anchorX);
                _anchorY += 0.1f * (salPeak / _coarseX - _anchorY);
                if (novStrength > _glanceTrigger * _novBaseline + 1e-6f)
                {
                    _glanceLeft = _glanceFrames;   // something new appeared — glance at it
                    focus = novPeak;
                }
                else
                {
                    var ax = Math.Clamp((int)MathF.Round(_anchorX), 0, _coarseX - 1);
                    var ay = Math.Clamp((int)MathF.Round(_anchorY), 0, _coarseY - 1);
                    focus = ay * _coarseX + ax;
                }
                _novBaseline += 0.05f * (novStrength - _novBaseline);   // adapt baseline only while resting
            }
            _prevLuma = (float[])luma.Clone();
        }
        else
        {
            var sal = _mode == AttentionMode.Novelty
                ? NoveltyGrid(luma, red, green, blue, width, height, mR, mG, mB)
                : SaliencyGrid(luma, red, green, blue, width, height, mR, mG, mB);
            focus = ArgMax(sal);
            if (_mode == AttentionMode.Saliency) _prevLuma = (float[])luma.Clone();
        }

        var pcx = (focus % _coarseX + 0.5f) / _coarseX * width;
        var pcy = (focus / _coarseX + 0.5f) / _coarseY * height;
        var wW = Math.Max(4, (int)(width * _windowFrac));
        var wH = Math.Max(4, (int)(height * _windowFrac));
        var x0 = Math.Clamp((int)(pcx - wW / 2f), 0, width - wW);
        var y0 = Math.Clamp((int)(pcy - wH / 2f), 0, height - wH);

        var (cl, cr, cg, cb) = Crop(luma, red, green, blue, width, x0, y0, wW, wH);
        return (_fovea.Process(cl, wW, wH, cr, cg, cb), x0, y0, wW, wH);
    }

    private static int ArgMax(float[] a)
    {
        var best = 0;
        for (var i = 1; i < a.Length; i++) if (a[i] > a[best]) best = i;
        return best;
    }

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

    private float[] NoveltyGrid(float[] luma, float[] red, float[] green, float[] blue, int width, int height, float mR, float mG, float mB)
    {
        if (_bgL is null)
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
