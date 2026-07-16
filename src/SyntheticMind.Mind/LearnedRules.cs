using System.Numerics.Tensors;

namespace SyntheticMind.Mind;

/// <summary>How a learned-state unit decides what its encoder should represent.</summary>
public enum EncoderDrive
{
    /// <summary>
    /// Hebbian / Sanger's rule (GHA). The encoder chases variance — it learns the principal
    /// components of its input. It CANNOT collapse: a constant output has zero variance, which is
    /// the one thing this rule flees from. This is a concrete answer to SCAFFOLD.md decision 6 —
    /// drive the encoder with something the model can't shrink to zero.
    /// </summary>
    Variance,

    /// <summary>
    /// The trapdoor. The encoder chases its own predictability — it moves each state toward what
    /// the predictor expected. The global optimum is "make the state constant": then it's
    /// perfectly predictable and perfectly useless. This is the failure mode BYOL/SimSiam/VICReg
    /// exist to prevent. Included as a live fixture, not a candidate: run it and watch.
    /// </summary>
    Predictability,

    /// <summary>
    /// Slowness (Slow Feature Analysis, Wiskott &amp; Sejnowski 2002). The encoder chases features
    /// that change as SLOWLY as possible — it minimizes the variance of its own tick-to-tick change.
    /// Where Variance grabs the loudest structure (usually the fast stuff), this grabs the most
    /// sluggish, which is exactly where a slow hidden cause hides.
    ///
    /// Collapse is prevented not by chasing variance but by force: the feature directions are kept
    /// orthonormal every tick (Gram-Schmidt), so the state is always a full-rank rotation of the
    /// input and cannot pile into one direction or vanish. Whitening first (divide each input dim
    /// by its spread) stops the rule from cheating by picking a dimension that's slow only because
    /// it's tiny.
    /// </summary>
    Slowness,
}

/// <summary>
/// v1: a unit whose STATE is learned, not a fixed delay line (contrast LinearDeltaRule).
///
/// It's a small predictive autoencoder, every part trained by a local online rule, no backprop:
///
///   history  ──encoder(GHA)──►  state  ──readout──►  reconstructed input
///                                 │
///                              predictor
///                                 │
///                                 ▼
///                          predicted next state ──readout──► predicted next INPUT
///
///   - encoder   W_e (k×D): Hebbian (Sanger) or self-prediction — see EncoderDrive
///   - predictor W_p (k×k): delta rule, learns state_t-1 → state_t
///   - readout   W_d (d×k): delta rule, learns state → current input frame
///
/// The readout matters for honesty: it lets the unit predict the next INPUT (a fixed target it
/// can't shrink), so its error is comparable to v0's baselines. Grade a self-predicting model in
/// its own state space and collapse scores perfectly; grade it against the actual input and
/// collapse scores terribly. Anchoring the metric is itself half of the anti-collapse story.
///
/// A short history is stacked before encoding so the state can represent velocity, not just
/// position — otherwise, like any Markov-position model, it could never beat copy-last.
/// </summary>
public sealed class LearnedPredictiveRule : IUnitRule
{
    private readonly int _d;          // input frame width
    private readonly int _history;    // frames stacked before encoding
    private readonly int _bigD;       // _d * _history
    private readonly int _k;          // state width
    private readonly EncoderDrive _drive;

    // Optional nonlinear expansion: a fixed set of random product features c_i·c_j appended to the
    // history before encoding. A frequency (or any relationship between time points) simply is not
    // present in a single reading — it only shows up in products across time. So a purely linear
    // encoder is blind to it. These products give the linear machinery something nonlinear to grip.
    private readonly int _quad;       // number of product features
    private readonly int _encInput;   // _bigD + _quad
    private readonly int[] _pairA;    // which buffer slots get multiplied together
    private readonly int[] _pairB;
    private readonly float[] _expanded;

    private readonly float[] _mean;      // running mean of the encoder input (for centering)
    private readonly float[] _variance;  // running variance per input dim (for Slowness whitening)
    private readonly float[] _prevEncode; // last tick's encode vector (for the Slowness derivative)
    private readonly float[] _buffer;    // stacked history, most recent frame first
    private readonly float[][] _we;   // encoder   k × encInput
    private readonly float[][] _wp;   // predictor k × k
    private readonly float[] _bp;     // predictor bias k
    private readonly float[][] _wd;   // readout   d × k
    private readonly float[] _bd;     // readout bias d  (without this it predicts a zero-mean
                                      // signal for a signal whose mean is ~0.5 — off by the mean)

    private readonly float _encRate, _predRate, _readRate, _meanRate;

    private float[] _state;           // s_t
    private float[] _stateThatPredicted;  // the state ŝ was computed from
    private float[] _predictedNextState;  // ŝ, made last tick
    private bool _primed;

    public LearnedPredictiveRule(
        int inputWidth,
        int stateWidth,
        EncoderDrive drive = EncoderDrive.Variance,
        int history = 3,
        float encoderRate = 0.005f,
        float predictorRate = 0.02f,
        float readoutRate = 0.02f,
        float meanRate = 0.002f,
        int quadraticFeatures = 0,
        int seed = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stateWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(history);
        ArgumentOutOfRangeException.ThrowIfNegative(quadraticFeatures);

        _d = inputWidth;
        _history = history;
        _bigD = inputWidth * history;
        _k = stateWidth;
        _drive = drive;
        _encRate = encoderRate;
        _predRate = predictorRate;
        _readRate = readoutRate;
        _meanRate = meanRate;

        _quad = quadraticFeatures;
        _encInput = _bigD + _quad;

        var rng = new Random(seed);

        // Fixed random pairs of history slots, chosen once. Reused every tick, so the nonlinear
        // features are consistent — the encoder can actually learn about them.
        _pairA = new int[_quad];
        _pairB = new int[_quad];
        for (var r = 0; r < _quad; r++)
        {
            _pairA[r] = rng.Next(_bigD);
            _pairB[r] = rng.Next(_bigD);
        }
        _expanded = new float[_encInput];

        _mean = new float[_encInput];
        _variance = new float[_encInput];
        _prevEncode = new float[_encInput];
        _buffer = new float[_bigD];

        _we = new float[_k][];
        for (var i = 0; i < _k; i++)
        {
            _we[i] = new float[_encInput];
            for (var j = 0; j < _encInput; j++) _we[i][j] = (float)(rng.NextDouble() * 2 - 1) * 0.1f;
        }

        _wp = new float[_k][];
        for (var i = 0; i < _k; i++) _wp[i] = new float[_k];   // predictor starts at zero
        _bp = new float[_k];

        _wd = new float[_d][];
        for (var i = 0; i < _d; i++) _wd[i] = new float[_k];   // readout starts at zero
        _bd = new float[_d];

        _state = new float[_k];
        _stateThatPredicted = new float[_k];
        _predictedNextState = new float[_k];
    }

    public string Name => _drive switch
    {
        EncoderDrive.Variance => "learned/hebbian",
        EncoderDrive.Slowness => "learned/slow-feature",
        _ => "learned/self-predict (fixture)",
    };
    public int InputWidth => _d;
    public int StateWidth => _k;

    public float[] Observe(float[] input, float[]? context)
    {
        // Slide the history window and drop the new frame in at the front.
        Array.Copy(_buffer, 0, _buffer, _d, _bigD - _d);
        Array.Copy(input, 0, _buffer, 0, _d);

        // Build the encoder input: the raw history, then the nonlinear product features on top.
        Array.Copy(_buffer, 0, _expanded, 0, _bigD);
        for (var r = 0; r < _quad; r++) _expanded[_bigD + r] = _buffer[_pairA[r]] * _buffer[_pairB[r]];

        for (var j = 0; j < _encInput; j++) _mean[j] += _meanRate * (_expanded[j] - _mean[j]);

        var centered = new float[_encInput];
        for (var j = 0; j < _encInput; j++) centered[j] = _expanded[j] - _mean[j];

        // Slowness works in a whitened space (each dim divided by its spread) so it can't cheat by
        // picking a dimension that's "slow" only because it barely moves. The other drives encode
        // the plain centered input, exactly as before.
        var encode = centered;
        if (_drive == EncoderDrive.Slowness)
        {
            for (var j = 0; j < _encInput; j++) _variance[j] += _meanRate * (centered[j] * centered[j] - _variance[j]);
            encode = new float[_encInput];
            for (var j = 0; j < _encInput; j++) encode[j] = centered[j] / MathF.Sqrt(_variance[j] + 1e-4f);
        }

        // Encode: state_i = we_i · encode
        var state = new float[_k];
        for (var i = 0; i < _k; i++) state[i] = TensorPrimitives.Dot<float>(_we[i], encode);

        // Train the predictor on (state that made the prediction) -> (state that actually arrived).
        if (_primed)
        {
            for (var i = 0; i < _k; i++)
            {
                var stateError = state[i] - _predictedNextState[i];
                var scale = _predRate * stateError;
                for (var j = 0; j < _k; j++) _wp[i][j] += scale * _stateThatPredicted[j];
                _bp[i] += scale;
            }
        }

        // Train the readout to reconstruct the current input frame from the current state.
        for (var i = 0; i < _d; i++)
        {
            var reconstruction = TensorPrimitives.Dot<float>(_wd[i], state) + _bd[i];
            var scale = _readRate * (input[i] - reconstruction);
            for (var j = 0; j < _k; j++) _wd[i][j] += scale * state[j];
            _bd[i] += scale;
        }

        UpdateEncoder(encode, state);
        Array.Copy(encode, _prevEncode, _encInput);

        _state = state;
        return (float[])state.Clone();
    }

    public float[] Predict(float[]? context)
    {
        // Predict next state from current state, then read it out into input space.
        var nextState = new float[_k];
        for (var i = 0; i < _k; i++)
            nextState[i] = TensorPrimitives.Dot<float>(_wp[i], _state) + _bp[i];

        _predictedNextState = nextState;
        _stateThatPredicted = _state;
        _primed = true;

        var prediction = new float[_d];
        for (var i = 0; i < _d; i++)
            prediction[i] = TensorPrimitives.Dot<float>(_wd[i], nextState) + _bd[i];
        return prediction;
    }

    /// <summary>All adaptation happens in Observe/Predict, where the freshly-encoded state
    /// exists. Nothing to do here.</summary>
    public void Learn(float[] predictionError) { }

    private void UpdateEncoder(float[] centered, float[] state)
    {
        switch (_drive)
        {
            case EncoderDrive.Variance:
                // Sanger's rule (GHA): Δw_i = η · s_i · (centered − Σ_{j≤i} s_j w_j).
                // The cumulative residual is what orthogonalizes the components and pins each to
                // a principal direction — which is why the state spreads across dimensions
                // instead of piling into one.
                var residual = (float[])centered.Clone();
                for (var i = 0; i < _k; i++)
                {
                    for (var j = 0; j < centered.Length; j++) residual[j] -= state[i] * _we[i][j];
                    var scale = _encRate * state[i];
                    for (var j = 0; j < centered.Length; j++) _we[i][j] += scale * residual[j];
                }
                break;

            case EncoderDrive.Predictability:
                // The trapdoor: move the encoding toward what was predicted. Reduces state-space
                // surprise by making the representation itself blander. Watch it in CollapseMonitor.
                if (_primed)
                {
                    for (var i = 0; i < _k; i++)
                    {
                        var scale = -_encRate * (state[i] - _predictedNextState[i]);
                        for (var j = 0; j < centered.Length; j++) _we[i][j] += scale * centered[j];
                    }
                }
                break;

            case EncoderDrive.Slowness:
                // Push each feature direction to make its OWN change smaller: gradient of the
                // squared feature-derivative is (w·d)·d, so step downhill along −(w·d)·d.
                var n = centered.Length;
                var d = new float[n];
                for (var j = 0; j < n; j++) d[j] = centered[j] - _prevEncode[j];
                for (var i = 0; i < _k; i++)
                {
                    var derivative = TensorPrimitives.Dot<float>(_we[i], d);
                    var scale = -_encRate * derivative;
                    for (var j = 0; j < n; j++) _we[i][j] += scale * d[j];
                }
                OrthonormalizeEncoderRows();  // the anti-collapse guarantee, enforced not hoped
                break;
        }
    }

    /// <summary>Gram-Schmidt the encoder rows to an orthonormal set. Keeps the Slowness features
    /// distinct and full-rank, which is what makes collapse impossible by construction.</summary>
    private void OrthonormalizeEncoderRows()
    {
        var n = _encInput;
        for (var i = 0; i < _k; i++)
        {
            var wi = _we[i];
            for (var p = 0; p < i; p++)
            {
                var dot = TensorPrimitives.Dot<float>(wi, _we[p]);
                for (var j = 0; j < n; j++) wi[j] -= dot * _we[p][j];
            }
            var norm = TensorPrimitives.Norm<float>(wi);
            if (norm > 1e-8f)
                for (var j = 0; j < n; j++) wi[j] /= norm;
        }
    }
}
