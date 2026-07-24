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
    private readonly AttentionMode _mode;
    private readonly int _glanceFrames;      // how long a glance at a held-up thing lasts

    // Live-tunable (finding: the tuner exposes these so attention can be fought in real time).
    /// <summary>Novelty spike (× baseline) that earns a glance. Lower = glances away more readily.</summary>
    public float GlanceTrigger { get; set; }
    /// <summary>Window side as a fraction of the frame.</summary>
    public float WindowFrac { get; set; }
    /// <summary>How fast the novelty background absorbs what it sees.</summary>
    public float BgRate { get; set; }
    /// <summary>0 = off; 1 = fully down-weight skin-toned pixels, so a non-skin object can out-compete a
    /// face or bare arm for attention. A crude R&gt;G&gt;B heuristic — dial to taste.</summary>
    public float SkinSuppress { get; set; }

    private float[]? _prevLuma;
    private float[]? _bgL, _bgR, _bgG, _bgB;  // running background (novelty)
    private float _anchorX, _anchorY;         // smoothed person location (coarse cells)
    private bool _anchorInit;
    private float _novBaseline;
    private bool _novInit;
    private int _glanceLeft;                  // frames remaining in the current glance
    private readonly List<float[]> _taughtProtos = new();   // normalized signatures of things you taught it to look at

    // Audio-visual correspondence: attend to the region whose motion syncs with the sound (a mouth
    // when talking). A short history of per-cell motion and of the sound energy, correlated per frame.
    private const int AvWindow = 12;
    private float[]? _motionHist;                          // [cell * AvWindow + t]
    private readonly float[] _audioHist = new float[AvWindow];
    private int _histPos, _histCount;
    /// <summary>Motion↔sound correlation needed to attend to the sound source. Lower = more eager.</summary>
    public float AvThreshold { get; set; } = 0.4f;

    public ObjectAttention(Retina fovea, int coarseX = 16, int coarseY = 12, float windowFrac = 0.5f,
        AttentionMode mode = AttentionMode.Saliency, float bgRate = 0.03f,
        int glanceFrames = 14, float glanceTrigger = 2.5f)
    {
        _fovea = fovea;
        _coarseX = coarseX;
        _coarseY = coarseY;
        _mode = mode;
        _glanceFrames = glanceFrames;
        WindowFrac = windowFrac;
        BgRate = bgRate;
        GlanceTrigger = glanceTrigger;
    }

    public int Width => _fovea.Width;

    /// <summary>True while attention is glancing away at a held-up/pointed-at thing (vs. resting on the person).</summary>
    public bool Glancing => _glanceLeft > 0;

    /// <summary>Describe an arbitrary region (a region a human pointed at) with the SAME fovea attention
    /// uses everywhere else — so a taught region is represented identically to the codebook units, and
    /// can be learned from and compared against them.</summary>
    public float[] Describe(float[] luma, float[] red, float[] green, float[] blue, int width, int x0, int y0, int w, int h)
    {
        var (cl, cr, cg, cb) = Crop(luma, red, green, blue, width, x0, y0, w, h);
        return _fovea.Process(cl, w, h, cr, cg, cb);
    }

    /// <summary>How many distinct "look here" things you've taught it.</summary>
    public int TaughtCount => _taughtProtos.Count;

    /// <summary>Learn a taught region (Stage 2): store its normalised signature, or refine an existing
    /// one if it's the same kind of thing — so pointing at the mug several times sharpens one "mug"
    /// signature rather than making many. Attention then gets pulled toward regions that match.</summary>
    public void LearnFocus(float[] features)
    {
        var u = Unit(features);
        var best = -1; var bestSim = -1f;
        for (var i = 0; i < _taughtProtos.Count; i++) { var s = Dot(u, _taughtProtos[i]); if (s > bestSim) { bestSim = s; best = i; } }
        if (best >= 0 && bestSim > 0.85f)
        {
            var p = _taughtProtos[best];
            for (var j = 0; j < p.Length; j++) p[j] = 0.8f * p[j] + 0.2f * u[j];
            Renorm(p);
        }
        else if (_taughtProtos.Count < 16) _taughtProtos.Add(u);
    }

    private float TaughtScore(float[] features)   // best cosine similarity to any taught thing (0 if none)
    {
        if (_taughtProtos.Count == 0) return 0f;
        var u = Unit(features);
        var best = 0f;
        foreach (var p in _taughtProtos) best = MathF.Max(best, Dot(u, p));
        return best;
    }

    /// <summary>Teach it to look at the region around a normalized point, described the SAME way the
    /// runtime scan describes candidates (a WindowFrac window) — so a taught thing actually matches
    /// when it reappears, regardless of how tightly you framed it.</summary>
    public void LearnFocusAt(float[] luma, float[] red, float[] green, float[] blue, int width, int height, float cxNorm, float cyNorm)
    {
        var (x0, y0, wW, hH) = WindowAt(cxNorm * width, cyNorm * height, width, height);
        LearnFocus(Describe(luma, red, green, blue, width, x0, y0, wW, hH));
    }

    /// <summary>Save/restore taught signatures so teaching survives a restart.</summary>
    public float[][] ExportTaught() => _taughtProtos.ToArray();
    public void ImportTaught(float[][] protos) { _taughtProtos.Clear(); _taughtProtos.AddRange(protos); }

    // Scan a GRID of window positions across the whole frame (not just the salient ones, so a taught
    // thing can be found even when it isn't salient) for the best match to something you taught.
    private int BestTaughtCell(float[] luma, float[] red, float[] green, float[] blue, int width, int height)
    {
        var best = -1; var bestScore = 0.7f;   // similarity floor to count as "a taught thing is here"
        for (var cy = 1; cy < _coarseY; cy += 2)
            for (var cx = 1; cx < _coarseX; cx += 2)
            {
                var cell = cy * _coarseX + cx;
                var (x0, y0, wW, hH) = CellWindow(cell, width, height);
                var s = TaughtScore(Describe(luma, red, green, blue, width, x0, y0, wW, hH));
                if (s > bestScore) { bestScore = s; best = cell; }
            }
        return best;
    }

    private (int X0, int Y0, int W, int H) WindowAt(float pcx, float pcy, int width, int height)
    {
        var wW = Math.Max(4, (int)(width * WindowFrac));
        var hH = Math.Max(4, (int)(height * WindowFrac));
        return (Math.Clamp((int)(pcx - wW / 2f), 0, width - wW), Math.Clamp((int)(pcy - hH / 2f), 0, height - hH), wW, hH);
    }

    private (int X0, int Y0, int W, int H) CellWindow(int cell, int width, int height)
        => WindowAt((cell % _coarseX + 0.5f) / _coarseX * width, (cell / _coarseX + 0.5f) / _coarseY * height, width, height);

    private static float[] Unit(float[] v)
    {
        var n = 0f; foreach (var x in v) n += x * x; n = MathF.Sqrt(n);
        var u = new float[v.Length]; if (n > 1e-9f) for (var i = 0; i < v.Length; i++) u[i] = v[i] / n;
        return u;
    }
    private static void Renorm(float[] v)
    {
        var n = 0f; foreach (var x in v) n += x * x; n = MathF.Sqrt(n);
        if (n > 1e-9f) for (var i = 0; i < v.Length; i++) v[i] /= n;
    }
    private static float Dot(float[] a, float[] b) { var s = 0f; for (var i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }

    // Push this frame's per-cell motion + the sound energy into the rolling history (aligned in time).
    private void PushAv(float[] luma, int width, int height, float audioEnergy)
    {
        var cells = _coarseX * _coarseY;
        _motionHist ??= new float[cells * AvWindow];
        if (_prevLuma is not null)
        {
            var mg = new float[cells];
            for (var y = 0; y < height; y++)
            {
                var cy = y * _coarseY / height;
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    mg[cy * _coarseX + x * _coarseX / width] += MathF.Abs(luma[i] - _prevLuma[i]);
                }
            }
            for (var c = 0; c < cells; c++) _motionHist[c * AvWindow + _histPos] = mg[c];
        }
        else for (var c = 0; c < cells; c++) _motionHist[c * AvWindow + _histPos] = 0f;

        _audioHist[_histPos] = audioEnergy;
        _histPos = (_histPos + 1) % AvWindow;
        if (_histCount < AvWindow) _histCount++;
    }

    // The cell whose motion history best correlates with the sound history — the sound's visual source.
    private int SoundSourceCell(out float bestCorr)
    {
        bestCorr = 0f;
        if (_histCount < AvWindow || _motionHist is null) return -1;

        var aMean = 0f;
        foreach (var a in _audioHist) aMean += a;
        aMean /= AvWindow;
        if (aMean < 1e-5f) return -1;   // essentially silence — no sound to look toward
        var aVar = 0f;
        foreach (var a in _audioHist) { var d = a - aMean; aVar += d * d; }
        if (aVar < 1e-9f) return -1;

        var cells = _coarseX * _coarseY;
        var best = -1;
        for (var c = 0; c < cells; c++)
        {
            var mMean = 0f;
            for (var t = 0; t < AvWindow; t++) mMean += _motionHist[c * AvWindow + t];
            mMean /= AvWindow;
            float mVar = 0f, cov = 0f;
            for (var t = 0; t < AvWindow; t++)
            {
                var dm = _motionHist[c * AvWindow + t] - mMean;
                cov += dm * (_audioHist[t] - aMean);
                mVar += dm * dm;
            }
            if (mVar < 1e-9f) continue;
            var corr = cov / MathF.Sqrt(mVar * aVar);
            if (corr > bestCorr) { bestCorr = corr; best = c; }
        }
        return best;
    }

    /// <param name="audioEnergy">The sound level right now — lets attention find the moving thing that
    /// syncs with the sound (audio-visual correspondence). 0 (default) disables it.</param>
    public (float[] Features, int X0, int Y0, int W, int H) Attend(
        float[] luma, float[] red, float[] green, float[] blue, int width, int height, float audioEnergy = 0f)
    {
        float mR = Mean(red), mG = Mean(green), mB = Mean(blue);
        int focus;

        PushAv(luma, width, height, audioEnergy);   // track motion↔sound history

        if (_mode == AttentionMode.PersonCentred)
        {
            var sal = SaliencyGrid(luma, red, green, blue, width, height, mR, mG, mB);   // the person (anchor)
            var nov = NoveltyGrid(luma, red, green, blue, width, height, mR, mG, mB);     // what just appeared
            var salPeak = ArgMax(sal);
            var novPeak = ArgMax(nov);
            var novStrength = nov[novPeak];
            if (!_novInit) { _novBaseline = novStrength; _novInit = true; }
            if (!_anchorInit) { _anchorX = salPeak % _coarseX; _anchorY = salPeak / _coarseX; _anchorInit = true; }

            // taught pull: if something you taught it to look at is clearly in view, go there.
            var taughtCell = _taughtProtos.Count > 0 ? BestTaughtCell(luma, red, green, blue, width, height) : -1;
            // audio-visual correspondence: the moving thing making the sound (a talking mouth).
            var soundCell = SoundSourceCell(out var corr);

            if (taughtCell >= 0)
            {
                focus = taughtCell;          // a taught thing wins over everything
                _glanceLeft = _glanceFrames;
            }
            else if (soundCell >= 0 && corr >= AvThreshold)
            {
                focus = soundCell;           // look at what's making the sound
                _glanceLeft = _glanceFrames;
            }
            else if (_glanceLeft > 0)
            {
                _glanceLeft--;               // keep looking at the thing it glanced to
                focus = novPeak;
            }
            else
            {
                // Resting on the person: drift the anchor toward the current salient (person) region.
                _anchorX += 0.1f * (salPeak % _coarseX - _anchorX);
                _anchorY += 0.1f * (salPeak / _coarseX - _anchorY);
                if (novStrength > GlanceTrigger * _novBaseline + 1e-6f)
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
        var wW = Math.Max(4, (int)(width * WindowFrac));
        var wH = Math.Max(4, (int)(height * WindowFrac));
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
                var s = edge + col + 0.5f * mot;
                if (SkinSuppress > 0) s *= 1f - SkinSuppress * Skin(red[i], green[i], blue[i]);
                sal[cy * _coarseX + x * _coarseX / width] += s;
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
                var s = nov + 0.25f * col;
                if (SkinSuppress > 0) s *= 1f - SkinSuppress * Skin(red[i], green[i], blue[i]);
                sal[cy * _coarseX + x * _coarseX / width] += s;

                _bgL[i] += BgRate * (luma[i] - _bgL[i]);
                _bgR![i] += BgRate * (red[i] - _bgR[i]);
                _bgG![i] += BgRate * (green[i] - _bgG[i]);
                _bgB![i] += BgRate * (blue[i] - _bgB[i]);
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

    // Crude, tone-agnostic-ish skin likelihood: human skin reflects more red than blue and is ordered
    // R>G>B across tones. Higher for redder-ordered pixels. Enough to down-weight face/arm vs a mug.
    private static float Skin(float r, float g, float b)
        => r > g && g >= b ? Math.Clamp((r - b) * 3f, 0f, 1f) : 0f;
}
