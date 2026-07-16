namespace SyntheticMind.Mind;

/// <summary>
/// A leaky integrator — an exponential moving average, one per channel. It holds a slowly-fading
/// memory of its input, which is how you turn a noisy, fast signal into a stable estimate of the
/// slow thing underneath it.
///
/// This is one of the two temporal primitives finding 008 identified as necessary for robustly
/// abstracting a slow latent — the other is change-sensing (<see cref="TemporalPool"/> in
/// change-energy mode). Finding 008's lesson: the max-variance learned encoder chases fast
/// variance and discards slow structure, so extracting a slow latent needs explicit temporal
/// machinery, not just a cleverer objective. Integration is that machinery.
///
/// `rate` is the fraction of the gap closed each step: small rate = long memory (slow, stable),
/// large rate = short memory (fast, twitchy). The effective time constant is ~1/rate steps.
/// </summary>
public sealed class LeakyIntegrator
{
    private readonly float[] _value;

    public LeakyIntegrator(int width, float rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        if (rate is <= 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(rate), "rate must be in (0, 1].");
        _value = new float[width];
        Rate = rate;
    }

    public float Rate { get; }

    /// <summary>Fold one sample in and return the current running estimate (a fresh copy).</summary>
    public float[] Push(float[] sample)
    {
        if (sample.Length != _value.Length)
            throw new ArgumentException($"Expected {_value.Length}-wide sample, got {sample.Length}.", nameof(sample));

        for (var i = 0; i < _value.Length; i++) _value[i] += Rate * (sample[i] - _value[i]);
        return (float[])_value.Clone();
    }
}
