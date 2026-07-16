namespace SyntheticMind.Audio;

/// <summary>
/// A plain iterative radix-2 Cooley–Tukey FFT. Owned rather than rented, because it's the core of
/// the cochlea and it's a well-understood algorithm we can test against known answers.
///
/// This is the one heavy piece of fixed signal processing the ear does before anything reaches the
/// brain: a frequency decomposition. It learns nothing; it just turns a slice of waveform into
/// "how much energy at each frequency."
/// </summary>
public static class Fft
{
    /// <summary>
    /// In-place forward FFT. <paramref name="real"/> and <paramref name="imag"/> must be the same
    /// length, which must be a power of two. On return they hold the transform.
    /// </summary>
    public static void Forward(float[] real, float[] imag)
    {
        var n = real.Length;
        if (imag.Length != n) throw new ArgumentException("real and imag must be the same length.");
        if (n == 0 || (n & (n - 1)) != 0) throw new ArgumentException($"length must be a power of two, got {n}.");

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // Butterflies, stage by stage.
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2.0 * Math.PI / len;
            var wReal = (float)Math.Cos(angle);
            var wImag = (float)Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                float curReal = 1f, curImag = 0f;
                for (var k = 0; k < len / 2; k++)
                {
                    var a = i + k;
                    var b = i + k + len / 2;
                    var tReal = real[b] * curReal - imag[b] * curImag;
                    var tImag = real[b] * curImag + imag[b] * curReal;
                    real[b] = real[a] - tReal;
                    imag[b] = imag[a] - tImag;
                    real[a] += tReal;
                    imag[a] += tImag;
                    var nextReal = curReal * wReal - curImag * wImag;
                    curImag = curReal * wImag + curImag * wReal;
                    curReal = nextReal;
                }
            }
        }
    }

    /// <summary>Power spectrum |X|² for the lower half (0..n/2), which is all a real signal carries.</summary>
    public static float[] PowerSpectrum(float[] samples)
    {
        var n = samples.Length;
        var real = (float[])samples.Clone();
        var imag = new float[n];
        Forward(real, imag);

        var half = n / 2 + 1;
        var power = new float[half];
        for (var i = 0; i < half; i++) power[i] = real[i] * real[i] + imag[i] * imag[i];
        return power;
    }
}
