using System.Numerics.Tensors;

namespace SyntheticMind.Mind;

/// <summary>
/// v0 of the real thing. State is a delay line of recent input; prediction is a linear map from
/// it; learning is the delta rule (Widrow-Hoff / LMS): w += rate * error * input.
///
/// That rule is genuinely local and genuinely online — no backprop, no batches, no shuffling.
/// It's also about the simplest thing that could possibly work, which is why it goes first:
/// if this can't beat "assume nothing changes" on a bouncing ball, the harness is broken and
/// we want to know that now rather than after building something clever.
///
/// **It cannot collapse.** The state is a fixed function of input — nothing learned decides
/// what to represent — so there's no degenerate solution to fall into. That's a property of
/// v0, not of the design. The moment state becomes learned (v1), collapse is live and
/// SCAFFOLD.md §7 becomes the whole problem.
/// </summary>
public sealed class LinearDeltaRule : IUnitRule
{
    private readonly float[] _state;      // delay line, most recent first
    private readonly float[][] _weights;  // [inputWidth][stateWidth + 1], last column is bias
    private readonly float _learningRate;

    public LinearDeltaRule(int inputWidth, int historyLength = 3, float learningRate = 0.05f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(historyLength);

        InputWidth = inputWidth;
        StateWidth = inputWidth * historyLength;
        _learningRate = learningRate;
        _state = new float[StateWidth];
        _weights = new float[inputWidth][];
        for (var i = 0; i < inputWidth; i++) _weights[i] = new float[StateWidth + 1];
    }

    public string Name => "linear-delta";
    public int InputWidth { get; }
    public int StateWidth { get; }

    public float[] Observe(float[] input, float[]? context)
    {
        // Shift the delay line down by one input-width, then write the new input at the front.
        Array.Copy(_state, 0, _state, InputWidth, StateWidth - InputWidth);
        Array.Copy(input, 0, _state, 0, InputWidth);
        return (float[])_state.Clone();
    }

    public float[] Predict(float[]? context)
    {
        var prediction = new float[InputWidth];
        for (var i = 0; i < InputWidth; i++)
            prediction[i] = TensorPrimitives.Dot<float>(_state, _weights[i].AsSpan(0, StateWidth))
                            + _weights[i][StateWidth];
        return prediction;
    }

    public void Learn(float[] error)
    {
        // Learn is called BEFORE Observe on each tick, so _state is still the state that
        // produced the prediction this error belongs to. That ordering is the whole
        // correctness argument for this method — see Unit.Observe.
        for (var i = 0; i < InputWidth; i++)
        {
            var scale = _learningRate * error[i];
            var row = _weights[i];
            for (var j = 0; j < StateWidth; j++) row[j] += scale * _state[j];
            row[StateWidth] += scale;
        }
    }
}

/// <summary>
/// Baseline: predict that the next input equals the current one.
///
/// This is the baseline that humbles people. On any smooth stream it is *very* hard to beat,
/// and a model that merely ties it has learned nothing at all while producing a beautiful
/// loss curve. Every result gets compared against this or it doesn't count.
/// </summary>
public sealed class CopyLastRule(int inputWidth) : IUnitRule
{
    private float[] _last = new float[inputWidth];

    public string Name => "copy-last";
    public int InputWidth => inputWidth;
    public int StateWidth => inputWidth;

    public float[] Observe(float[] input, float[]? context)
    {
        _last = (float[])input.Clone();
        return _last;
    }

    public float[] Predict(float[]? context) => (float[])_last.Clone();
    public void Learn(float[] error) { }
}

/// <summary>Baseline: predict the running mean. Deliberately weak — the floor, not the bar.</summary>
public sealed class MeanRule(int inputWidth) : IUnitRule
{
    private readonly float[] _sum = new float[inputWidth];
    private int _count;

    public string Name => "running-mean";
    public int InputWidth => inputWidth;
    public int StateWidth => inputWidth;

    public float[] Observe(float[] input, float[]? context)
    {
        TensorPrimitives.Add<float>(_sum, input, _sum);
        _count++;
        return Predict(context);
    }

    public float[] Predict(float[]? context)
    {
        if (_count == 0) return new float[inputWidth];
        var mean = new float[inputWidth];
        TensorPrimitives.Divide<float>(_sum, _count, mean);
        return mean;
    }

    public void Learn(float[] error) { }
}

/// <summary>
/// Deliberately collapsed. Publishes a constant state regardless of input, and can only learn
/// a constant prediction (the bias term).
///
/// This is a test fixture, not a candidate. On ConstantStream it achieves near-zero prediction
/// error while carrying exactly zero information — which is precisely what collapse looks like
/// from a loss curve, and precisely why a loss curve is not enough.
///
/// **If CollapseMetrics can't catch this, it can't catch anything.**
/// </summary>
public sealed class CollapsedRule(int inputWidth, float learningRate = 0.05f) : IUnitRule
{
    private readonly float[] _bias = new float[inputWidth];

    public string Name => "collapsed (fixture)";
    public int InputWidth => inputWidth;
    public int StateWidth => inputWidth;

    public float[] Observe(float[] input, float[]? context) => new float[inputWidth];
    public float[] Predict(float[]? context) => (float[])_bias.Clone();

    public void Learn(float[] error)
    {
        for (var i = 0; i < inputWidth; i++) _bias[i] += learningRate * error[i];
    }
}
