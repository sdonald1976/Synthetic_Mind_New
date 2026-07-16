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

    private readonly float[] _mean;   // running mean of the stacked history (for centering)
    private readonly float[] _buffer; // stacked history, most recent frame first
    private readonly float[][] _we;   // encoder   k × bigD
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
        int seed = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stateWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(history);

        _d = inputWidth;
        _history = history;
        _bigD = inputWidth * history;
        _k = stateWidth;
        _drive = drive;
        _encRate = encoderRate;
        _predRate = predictorRate;
        _readRate = readoutRate;
        _meanRate = meanRate;

        _mean = new float[_bigD];
        _buffer = new float[_bigD];

        var rng = new Random(seed);
        _we = new float[_k][];
        for (var i = 0; i < _k; i++)
        {
            _we[i] = new float[_bigD];
            for (var j = 0; j < _bigD; j++) _we[i][j] = (float)(rng.NextDouble() * 2 - 1) * 0.1f;
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

    public string Name => _drive == EncoderDrive.Variance ? "learned/hebbian" : "learned/self-predict (fixture)";
    public int InputWidth => _d;
    public int StateWidth => _k;

    public float[] Observe(float[] input, float[]? context)
    {
        // Slide the history window and drop the new frame in at the front.
        Array.Copy(_buffer, 0, _buffer, _d, _bigD - _d);
        Array.Copy(input, 0, _buffer, 0, _d);

        for (var j = 0; j < _bigD; j++) _mean[j] += _meanRate * (_buffer[j] - _mean[j]);

        var centered = new float[_bigD];
        for (var j = 0; j < _bigD; j++) centered[j] = _buffer[j] - _mean[j];

        // Encode: state_i = we_i · centered
        var state = new float[_k];
        for (var i = 0; i < _k; i++) state[i] = TensorPrimitives.Dot<float>(_we[i], centered);

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

        UpdateEncoder(centered, state);

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
                    for (var j = 0; j < _bigD; j++) residual[j] -= state[i] * _we[i][j];
                    var scale = _encRate * state[i];
                    for (var j = 0; j < _bigD; j++) _we[i][j] += scale * residual[j];
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
                        for (var j = 0; j < _bigD; j++) _we[i][j] += scale * centered[j];
                    }
                }
                break;
        }
    }
}
