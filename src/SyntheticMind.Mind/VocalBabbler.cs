namespace SyntheticMind.Mind;

/// <summary>
/// Learns to use a mouth by babbling — the action loop the project kept circling (ARCHITECTURE §5:
/// perception → ACTION → perception, learned from error, no teacher). It knows nothing about its own
/// vocal tract at the start. It tries random controls, <em>hears itself</em> through the very same ear
/// it perceives the world with (a <c>hear</c> delegate: controls → mel), and:
///   1. learns a FORWARD model of its voice (controls → sound), error falling as it babbles; and
///   2. remembers every (controls, sound) it has made — a sensorimotor map.
/// Then it can IMITATE: given a target sound, recall the babble that came closest and refine it by
/// listening, producing controls whose sound matches. This is how infants bootstrap speech —
/// babble, hear, then reproduce what they hear (not from labels, from their own voice).
/// </summary>
public sealed class VocalBabbler
{
    private readonly Func<float[], float[]> _hear;   // controls → mel (the real synth + cochlea)
    private readonly int _controlDim;
    private readonly int _melDim;
    private readonly NlmsRegressor _forward;
    private readonly List<(float[] Control, float[] Mel)> _memory = [];
    private readonly Random _rng;

    public VocalBabbler(int controlDim, int melDim, Func<float[], float[]> hear, int seed = 1)
    {
        _controlDim = controlDim;
        _melDim = melDim;
        _hear = hear;
        _rng = new Random(seed);
        _forward = new NlmsRegressor(FeatureCount(controlDim), melDim);
    }

    public int BabbleCount => _memory.Count;

    /// <summary>One babble: pick random controls, hear the result, remember it, and update the forward
    /// model. Returns the forward model's squared error on this babble BEFORE learning — watch it
    /// fall as the map of "what my mouth does" firms up.</summary>
    public float Babble()
    {
        var control = new float[_controlDim];
        for (var i = 0; i < _controlDim; i++) control[i] = (float)_rng.NextDouble();
        var mel = _hear(control);
        _memory.Add((control, mel));
        return _forward.Learn(Features(control), mel);
    }

    /// <summary>The forward model's prediction of the sound a control setting will make (no listening).</summary>
    public float[] Predict(float[] control) => _forward.Predict(Features(control));

    /// <summary>
    /// Imitate a target sound: recall the closest babble, then hill-climb by actually listening —
    /// perturb the controls, keep the change if it sounds nearer the target. Returns the chosen
    /// controls, the sound they actually make, and its distance to the target.
    /// </summary>
    public (float[] Control, float[] Mel, float Distance) Imitate(float[] targetMel, int refineSteps = 60)
    {
        if (_memory.Count == 0) throw new InvalidOperationException("Babble before trying to imitate.");

        // Start from the nearest thing it has ever said.
        var best = _memory[0].Control;
        var bestMel = _memory[0].Mel;
        var bestDist = Distance(bestMel, targetMel);
        for (var i = 1; i < _memory.Count; i++)
        {
            var d = Distance(_memory[i].Mel, targetMel);
            if (d < bestDist) { bestDist = d; best = _memory[i].Control; bestMel = _memory[i].Mel; }
        }

        // Refine by listening: shrinking random nudges, accept the ones that get closer.
        var step = 0.25f;
        for (var s = 0; s < refineSteps; s++)
        {
            var trial = (float[])best.Clone();
            for (var i = 0; i < _controlDim; i++)
                trial[i] = Math.Clamp(trial[i] + step * (float)(_rng.NextDouble() * 2 - 1), 0f, 1f);
            var trialMel = _hear(trial);
            var d = Distance(trialMel, targetMel);
            if (d < bestDist) { bestDist = d; best = trial; bestMel = trialMel; }
            if (s % 12 == 11) step *= 0.6f;   // anneal
        }
        return (best, bestMel, bestDist);
    }

    /// <summary>Baseline for honesty: the average distance from the target to sounds it has babbled —
    /// i.e. how well a <em>random</em> attempt would do. Imitation is only meaningful if it beats this.</summary>
    public float ChanceDistance(float[] targetMel)
    {
        if (_memory.Count == 0) return float.NaN;
        var sum = 0f;
        foreach (var (_, mel) in _memory) sum += Distance(mel, targetMel);
        return sum / _memory.Count;
    }

    // Quadratic feature expansion of the controls, so a linear regressor can fit the curved
    // controls→sound map: [1, c_i, c_i·c_j].
    private static int FeatureCount(int n) => 1 + n + n * (n + 1) / 2;

    private float[] Features(float[] c)
    {
        var f = new float[FeatureCount(_controlDim)];
        var k = 0;
        f[k++] = 1f;
        for (var i = 0; i < _controlDim; i++) f[k++] = c[i];
        for (var i = 0; i < _controlDim; i++)
            for (var j = i; j < _controlDim; j++) f[k++] = c[i] * c[j];
        return f;
    }

    private static float Distance(float[] a, float[] b)
    {
        var d = 0f;
        for (var i = 0; i < a.Length; i++) { var e = a[i] - b[i]; d += e * e; }
        return MathF.Sqrt(d);
    }
}
