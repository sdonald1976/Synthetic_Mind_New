using SyntheticMind.Audio;

namespace SyntheticMind.Tests;

/// <summary>
/// The cochlea is rented-in-spirit signal processing, so it gets tested against answers we can work
/// out by hand: an impulse is flat, a pure tone is a spike, a low tone lights a low band. If these
/// are wrong, every downstream "the model heard X" is meaningless.
/// </summary>
public class AudioTests
{
    [Fact]
    public void Fft_of_an_impulse_is_flat()
    {
        // FFT of a unit impulse is all-ones across the spectrum — the canonical sanity check.
        var samples = new float[16];
        samples[0] = 1f;
        var power = Fft.PowerSpectrum(samples);
        Assert.All(power, p => Assert.Equal(1f, p, 0.0001f));
    }

    [Fact]
    public void Fft_of_a_pure_tone_peaks_at_its_bin()
    {
        // A cosine at exactly bin k should put (almost) all energy in bin k.
        const int n = 64;
        const int k = 5;
        var samples = new float[n];
        for (var i = 0; i < n; i++) samples[i] = MathF.Cos(2f * MathF.PI * k * i / n);

        var power = Fft.PowerSpectrum(samples);

        var peak = 0;
        for (var i = 1; i < power.Length; i++) if (power[i] > power[peak]) peak = i;
        Assert.Equal(k, peak);
    }

    [Fact]
    public void A_pure_tone_lights_the_band_containing_it()
    {
        const int sampleRate = 16000;
        var cochlea = new Cochlea(sampleRate, fftSize: 512, melBands: 20);

        var lowBand = DominantBand(cochlea, sampleRate, toneHz: 300f);
        var highBand = DominantBand(cochlea, sampleRate, toneHz: 4000f);

        // A low tone should dominate a lower band than a high tone — the mel axis is ordered.
        Assert.True(lowBand < highBand, $"300Hz lit band {lowBand}, 4000Hz lit band {highBand}");
    }

    [Fact]
    public void Silence_produces_near_zero_energy()
    {
        var cochlea = new Cochlea(16000, fftSize: 512, melBands: 20);
        var output = cochlea.Process(new float[512]);
        Assert.All(output, e => Assert.True(e < 0.001f, $"silence should be ~0, got {e}"));
    }

    [Fact]
    public void Louder_is_larger()
    {
        const int sampleRate = 16000;
        var cochlea = new Cochlea(sampleRate, fftSize: 512, melBands: 20);

        var quiet = cochlea.Process(Tone(sampleRate, 512, 1000f, amplitude: 0.1f));
        var loud = cochlea.Process(Tone(sampleRate, 512, 1000f, amplitude: 1.0f));

        Assert.True(loud.Max() > quiet.Max(), "a louder tone should register more energy");
    }

    private static float[] Tone(int sampleRate, int n, float hz, float amplitude = 1f)
    {
        var samples = new float[n];
        for (var i = 0; i < n; i++) samples[i] = amplitude * MathF.Sin(2f * MathF.PI * hz * i / sampleRate);
        return samples;
    }

    private static int DominantBand(Cochlea cochlea, int sampleRate, float toneHz)
    {
        var output = cochlea.Process(Tone(sampleRate, cochlea.FftSize, toneHz));
        var best = 0;
        for (var i = 1; i < output.Length; i++) if (output[i] > output[best]) best = i;
        return best;
    }
}
