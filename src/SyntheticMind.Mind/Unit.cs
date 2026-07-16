using System.Numerics.Tensors;

namespace SyntheticMind.Mind;

/// <summary>What a unit did on one tick. Everything the harness needs to judge it.</summary>
public readonly record struct Tick(float[] State, float[] Prediction, float[] Error)
{
    /// <summary>Mean squared error of the prediction that was made BEFORE this input arrived.</summary>
    public float SquaredError
    {
        get
        {
            var total = 0f;
            foreach (var e in Error) total += e * e;
            return total / Error.Length;
        }
    }
}

/// <summary>
/// One level of the hierarchy. The same unit at every level — SCAFFOLD.md §2.
///
/// It knows nothing about modality, task, or where it sits in the stack. It has four ports
/// (input from below, context from above, state to above, prediction to below) and does four
/// things forever: predict, compare, learn, publish.
///
/// The tick order matters and is easy to get subtly wrong:
///   1. a prediction for THIS input was made on the PREVIOUS tick
///   2. input arrives; error = input - that old prediction
///   3. learn from the error
///   4. fold input into state
///   5. predict the NEXT input from the new state
///
/// Predicting before folding the input in would be predicting the present, not the future.
/// That reads as spectacular performance and means nothing. See NoiseStream.
/// </summary>
public sealed class Unit(IUnitRule rule)
{
    private float[] _prediction = new float[rule.InputWidth];

    public string Name => rule.Name;
    public int InputWidth => rule.InputWidth;
    public int StateWidth => rule.StateWidth;

    public Tick Observe(float[] input, float[]? context = null)
    {
        if (input.Length != rule.InputWidth)
            throw new ArgumentException($"Unit expects {rule.InputWidth}-wide input, got {input.Length}.", nameof(input));

        var error = new float[input.Length];
        TensorPrimitives.Subtract<float>(input, _prediction, error);

        rule.Learn(error);
        var state = rule.Observe(input, context);
        _prediction = rule.Predict(context);

        return new Tick(state, _prediction, error);
    }
}
