namespace SyntheticMind.Mind;

/// <summary>How much information a unit's state actually carries.</summary>
/// <param name="MeanVariance">Average per-dimension variance. Zero means literally constant.</param>
/// <param name="ParticipationRatio">
/// Effective number of dimensions actually in use. 1.0 means every dimension is a scalar multiple
/// of one direction — the state is a line, however many dimensions it nominally has.
/// </param>
/// <param name="Width">Nominal state width.</param>
public readonly record struct CollapseReport(float MeanVariance, float ParticipationRatio, int Width)
{
    /// <summary>1.0 = using every dimension. Near 1/Width = collapsed to a line.</summary>
    public float RankFraction => Width == 0 ? 0f : ParticipationRatio / Width;

    /// <summary>
    /// Deliberately blunt. Subtle partial collapse needs a human looking at RankFraction;
    /// this only catches the unambiguous cases.
    /// </summary>
    public bool Collapsed => MeanVariance < 1e-6f || ParticipationRatio < 1.5f;

    public override string ToString() =>
        $"var {MeanVariance:E2}  rank {ParticipationRatio:F2}/{Width} ({RankFraction:P0}){(Collapsed ? "  COLLAPSED" : "")}";
}

/// <summary>
/// Watches a unit's published state and reports whether it's carrying information.
///
/// This exists because of SCAFFOLD.md §7: the instant a model predicts its own representation,
/// the cheapest winning move is to make that representation constant. Prediction error goes to
/// zero, the loss curve looks magnificent, and the model has learned nothing whatsoever.
/// **A collapsed model and a perfect model are indistinguishable from loss alone.**
///
/// So loss is never reported without this next to it.
///
/// The measure is the participation ratio of the state covariance:
///
///     PR = trace(C)^2 / ||C||_F^2
///
/// which equals (sum of eigenvalues)^2 / (sum of squared eigenvalues) — the effective rank —
/// but computes from plain sums, with no eigensolver. It ranges from 1 (all variance in a
/// single direction) to Width (variance spread evenly).
/// </summary>
public sealed class CollapseMonitor(int width)
{
    private readonly List<float[]> _samples = [];

    public int Count => _samples.Count;

    public void Record(float[] state)
    {
        if (state.Length != width)
            throw new ArgumentException($"Expected {width}-wide state, got {state.Length}.", nameof(state));
        _samples.Add((float[])state.Clone());
    }

    public CollapseReport Report()
    {
        if (_samples.Count < 2) return new CollapseReport(0f, 0f, width);

        var n = _samples.Count;

        var means = new double[width];
        foreach (var sample in _samples)
            for (var i = 0; i < width; i++) means[i] += sample[i];
        for (var i = 0; i < width; i++) means[i] /= n;

        // Full covariance. O(width^2) memory — fine for lab-scale state widths, and it has to
        // be the full matrix: per-dimension variance alone misses the case where every
        // dimension wiggles healthily but they're all the same wiggle.
        var covariance = new double[width, width];
        foreach (var sample in _samples)
        {
            for (var i = 0; i < width; i++)
            {
                var di = sample[i] - means[i];
                for (var j = 0; j < width; j++)
                    covariance[i, j] += di * (sample[j] - means[j]);
            }
        }

        var trace = 0.0;
        var frobeniusSquared = 0.0;
        for (var i = 0; i < width; i++)
        {
            for (var j = 0; j < width; j++)
            {
                covariance[i, j] /= n;
                frobeniusSquared += covariance[i, j] * covariance[i, j];
            }
            trace += covariance[i, i];
        }

        var meanVariance = (float)(trace / width);
        var participationRatio = frobeniusSquared < 1e-30 ? 0f : (float)(trace * trace / frobeniusSquared);

        return new CollapseReport(meanVariance, participationRatio, width);
    }

    public void Clear() => _samples.Clear();
}
