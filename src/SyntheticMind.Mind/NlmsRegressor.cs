namespace SyntheticMind.Mind;

/// <summary>
/// A plain online linear regressor trained by NLMS (normalized least-mean-squares) — the same
/// scale-invariant update the encoders use (finding 013), here mapping one vector to another. On its
/// own it is linear; feed it expanded (e.g. quadratic) features and it fits curved maps. The babbler
/// uses it as a <em>forward model</em> of its own voice: predict the sound (mel) a given set of vocal
/// controls will make, learning from the error each time it actually hears itself.
/// </summary>
public sealed class NlmsRegressor
{
    private readonly float[][] _w;   // [output][input]
    private readonly float _rate;
    private readonly float _eps;

    public NlmsRegressor(int inputDim, int outputDim, float rate = 0.5f, float eps = 1e-3f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputDim);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputDim);
        _rate = rate;
        _eps = eps;
        _w = new float[outputDim][];
        for (var o = 0; o < outputDim; o++) _w[o] = new float[inputDim];
    }

    public int InputDim => _w[0].Length;
    public int OutputDim => _w.Length;

    public float[] Predict(float[] x)
    {
        var y = new float[_w.Length];
        for (var o = 0; o < _w.Length; o++)
        {
            var s = 0f;
            var row = _w[o];
            for (var i = 0; i < row.Length; i++) s += row[i] * x[i];
            y[o] = s;
        }
        return y;
    }

    /// <summary>Learn one (input → target) pair. Returns the squared error BEFORE this update, so a
    /// caller can watch it fall as the model learns.</summary>
    public float Learn(float[] x, float[] target)
    {
        var pred = Predict(x);
        var norm = _eps;
        for (var i = 0; i < x.Length; i++) norm += x[i] * x[i];

        var err2 = 0f;
        for (var o = 0; o < _w.Length; o++)
        {
            var e = target[o] - pred[o];
            err2 += e * e;
            var step = _rate * e / norm;
            var row = _w[o];
            for (var i = 0; i < row.Length; i++) row[i] += step * x[i];
        }
        return err2;
    }
}
