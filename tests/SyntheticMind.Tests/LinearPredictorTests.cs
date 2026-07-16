using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Confirms the "learn on top" component learns at all, on a mapping with a known answer, before
/// trusting it inside the hierarchy.
/// </summary>
public class LinearPredictorTests
{
    [Fact]
    public void Learns_a_known_linear_map()
    {
        // Target: y0 = 2*x0 - x1 + 0.5 ; y1 = x0 + 0.3*x2. A delta-rule learner should nail this.
        var predictor = new LinearPredictor(inputWidth: 3, outputWidth: 2, rate: 0.05f);
        var random = new Random(1);

        float[] Target(float[] x) => [2f * x[0] - x[1] + 0.5f, x[0] + 0.3f * x[2]];

        float lastError = 0f;
        for (var step = 0; step < 20_000; step++)
        {
            var x = new[] { (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble() };
            var err = predictor.Learn(x, Target(x));
            lastError = MathF.Abs(err[0]) + MathF.Abs(err[1]);
        }

        Assert.True(lastError < 0.05f, $"predictor should have converged, final error {lastError:F4}");

        // And it generalizes to an unseen input.
        var test = new[] { 0.3f, 0.7f, 0.2f };
        var prediction = predictor.Predict(test);
        var expected = Target(test);
        Assert.Equal(expected[0], prediction[0], 0.05f);
        Assert.Equal(expected[1], prediction[1], 0.05f);
    }
}
