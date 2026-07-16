using System.Numerics.Tensors;

namespace SyntheticMind.Mind;

/// <summary>
/// An online linear predictor trained by the delta rule (Widrow-Hoff): the "learn on top" piece.
///
/// Finding 008 found that a learned max-variance ENCODER destroys a slow signal (it turned a
/// 0.5-correlated input into 0.00 — it chases fast variance and discards slow structure). A learned
/// READOUT is a different animal: it's trained toward a target, not toward variance, so it happily
/// carries whatever in its input predicts that target — including slow structure. That's why the
/// thing that learns on top of a <see cref="TemporalLevel"/> is a predictor, not an encoder.
///
/// No backprop, no batches, fully online — the same discipline as everything else here. Learning is
/// w += rate · error · input, one step per observation.
/// </summary>
public sealed class LinearPredictor
{
    private readonly float[][] _weights;  // [outputWidth][inputWidth]
    private readonly float[] _bias;       // [outputWidth]
    private readonly float _rate;

    public LinearPredictor(int inputWidth, int outputWidth, float rate = 0.02f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputWidth);

        InputWidth = inputWidth;
        OutputWidth = outputWidth;
        _rate = rate;
        _weights = new float[outputWidth][];
        for (var i = 0; i < outputWidth; i++) _weights[i] = new float[inputWidth];
        _bias = new float[outputWidth];
    }

    public int InputWidth { get; }
    public int OutputWidth { get; }

    /// <summary>Predict the target from an input vector.</summary>
    public float[] Predict(float[] input)
    {
        if (input.Length != InputWidth)
            throw new ArgumentException($"Expected {InputWidth}-wide input, got {input.Length}.", nameof(input));

        var output = new float[OutputWidth];
        for (var i = 0; i < OutputWidth; i++)
            output[i] = TensorPrimitives.Dot<float>(_weights[i], input) + _bias[i];
        return output;
    }

    /// <summary>Nudge the weights so this input maps closer to this target. Returns the prediction
    /// error that was corrected (target − prediction), which is itself a useful signal.</summary>
    public float[] Learn(float[] input, float[] target)
    {
        if (target.Length != OutputWidth)
            throw new ArgumentException($"Expected {OutputWidth}-wide target, got {target.Length}.", nameof(target));

        var prediction = Predict(input);
        var error = new float[OutputWidth];
        for (var i = 0; i < OutputWidth; i++)
        {
            error[i] = target[i] - prediction[i];
            var scale = _rate * error[i];
            var row = _weights[i];
            for (var j = 0; j < InputWidth; j++) row[j] += scale * input[j];
            _bias[i] += scale;
        }
        return error;
    }
}
