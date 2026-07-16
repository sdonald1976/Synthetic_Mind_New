using SyntheticMind.Mind;

namespace SyntheticMind.Audio;

/// <summary>
/// The ear's fixed front-end: waveform in, a vector of mel-band energies out. This is to audio what
/// the retina is to vision (SCAFFOLD.md §4) — dumb, fixed, learns nothing. Its only job is to hand
/// the model a frequency decomposition instead of raw samples, because a fast raw waveform is as
/// hopeless to predict directly as raw pixels.
///
/// Per frame: apply a Hann window (so the FFT doesn't see spectral leakage from the hard edges),
/// take the power spectrum, sum it through triangular filters spaced on the MEL scale (which packs
/// more resolution into low frequencies, roughly as human hearing does), and take a log (so the
/// huge dynamic range of loudness becomes additive, again as hearing does).
/// </summary>
public sealed class Cochlea
{
    private readonly int _fftSize;
    private readonly float[] _window;      // Hann window
    private readonly float[][] _melFilters; // [band][spectrum bin] triangular weights

    public Cochlea(int sampleRate = 16000, int fftSize = 512, int melBands = 20,
        float minHz = 50f, float? maxHz = null)
    {
        if ((fftSize & (fftSize - 1)) != 0) throw new ArgumentException("fftSize must be a power of two.", nameof(fftSize));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(melBands);

        _fftSize = fftSize;
        MelBands = melBands;
        var top = maxHz ?? sampleRate / 2f;

        _window = new float[fftSize];
        for (var i = 0; i < fftSize; i++)
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (fftSize - 1));

        _melFilters = BuildMelFilters(sampleRate, fftSize, melBands, minHz, top);
    }

    public int FftSize => _fftSize;
    public int MelBands { get; }

    /// <summary>
    /// Turn one frame of <see cref="FftSize"/> samples into a mel-energy vector. Log-compressed, so
    /// silence is near zero and loud bands are larger but not explosively so.
    /// </summary>
    public float[] Process(float[] frame)
    {
        if (frame.Length != _fftSize)
            throw new ArgumentException($"Expected a {_fftSize}-sample frame, got {frame.Length}.", nameof(frame));

        var windowed = new float[_fftSize];
        for (var i = 0; i < _fftSize; i++) windowed[i] = frame[i] * _window[i];

        var power = Fft.PowerSpectrum(windowed);

        var output = new float[MelBands];
        for (var band = 0; band < MelBands; band++)
        {
            var filter = _melFilters[band];
            var sum = 0f;
            for (var bin = 0; bin < filter.Length; bin++) sum += power[bin] * filter[bin];
            output[band] = MathF.Log(1f + sum);   // log compression, offset so 0 energy → 0
        }
        return output;
    }

    private static float HzToMel(float hz) => 2595f * MathF.Log10(1f + hz / 700f);
    private static float MelToHz(float mel) => 700f * (MathF.Pow(10f, mel / 2595f) - 1f);

    private static float[][] BuildMelFilters(int sampleRate, int fftSize, int melBands, float minHz, float maxHz)
    {
        var bins = fftSize / 2 + 1;

        // melBands triangular filters need melBands+2 edge points, evenly spaced on the mel scale.
        var minMel = HzToMel(minHz);
        var maxMel = HzToMel(maxHz);
        var edgeBins = new int[melBands + 2];
        for (var i = 0; i < edgeBins.Length; i++)
        {
            var mel = minMel + (maxMel - minMel) * i / (melBands + 1);
            var hz = MelToHz(mel);
            edgeBins[i] = (int)MathF.Round(hz / (sampleRate / 2f) * (bins - 1));
        }

        var filters = new float[melBands][];
        for (var band = 0; band < melBands; band++)
        {
            var lower = edgeBins[band];
            var center = edgeBins[band + 1];
            var upper = edgeBins[band + 2];
            var filter = new float[bins];

            for (var bin = lower; bin < center; bin++)
                if (center > lower) filter[bin] = (bin - lower) / (float)(center - lower);
            for (var bin = center; bin < upper; bin++)
                if (upper > center) filter[bin] = (upper - bin) / (float)(upper - center);

            filters[band] = filter;
        }
        return filters;
    }
}

/// <summary>
/// Streams overlapping frames from a source of raw samples through the <see cref="Cochlea"/>,
/// producing one mel-vector per hop — the perception branch's output, ready for a hierarchy.
/// The source is anything that yields samples in order: a WAV file now, a microphone next.
/// </summary>
public sealed class AudioStream : IStream
{
    private readonly Func<float[]?> _pullSamples; // returns a block of samples, or null at end
    private readonly Cochlea _cochlea;
    private readonly int _hop;
    private readonly bool _normalize;
    private readonly float _normRate;
    private readonly float[] _buffer;
    private readonly float[] _mean;
    private readonly float[] _variance;
    private float[] _pending = [];
    private int _pendingPos;

    /// <param name="normalize">Adaptive gain control — standardize each mel band by its running
    /// mean and spread. Real cochleas/retinas do this, and it keeps the downstream encoder from
    /// diverging when loudness varies (findings 008/011: the learning rate must match the input
    /// scale; normalizing the input fixes the scale once, for good).</param>
    /// <param name="normalizeRate">Leak of the gain control. Must be SLOW relative to the structure
    /// you care about, or it cancels the very signal it's normalizing (the mean-tracking trap).</param>
    public AudioStream(Func<float[]?> pullSamples, Cochlea cochlea, int hop = 160,
        bool normalize = true, float normalizeRate = 0.001f)
    {
        _pullSamples = pullSamples;
        _cochlea = cochlea;
        _hop = hop;
        _normalize = normalize;
        _normRate = normalizeRate;
        _buffer = new float[cochlea.FftSize];
        _mean = new float[cochlea.MelBands];
        _variance = new float[cochlea.MelBands];
        for (var i = 0; i < _variance.Length; i++) _variance[i] = 1f;
    }

    public string Name => "audio";
    public int Width => _cochlea.MelBands;

    public float[] Next()
    {
        // Slide the window forward by one hop, refill with new samples, emit a mel frame.
        Array.Copy(_buffer, _hop, _buffer, 0, _buffer.Length - _hop);
        for (var i = _buffer.Length - _hop; i < _buffer.Length; i++) _buffer[i] = NextSample();

        var mel = _cochlea.Process(_buffer);
        if (!_normalize) return mel;

        var normalized = new float[mel.Length];
        for (var i = 0; i < mel.Length; i++)
        {
            _mean[i] += _normRate * (mel[i] - _mean[i]);
            var centered = mel[i] - _mean[i];
            _variance[i] += _normRate * (centered * centered - _variance[i]);
            normalized[i] = centered / MathF.Sqrt(_variance[i] + 1e-6f);
        }
        return normalized;
    }

    public void Reset()
    {
        _pending = [];
        _pendingPos = 0;
        Array.Clear(_buffer);
        Array.Clear(_mean);
        for (var i = 0; i < _variance.Length; i++) _variance[i] = 1f;
    }

    private float NextSample()
    {
        if (_pendingPos >= _pending.Length)
        {
            _pending = _pullSamples() ?? new float[_hop]; // silence at end of source
            _pendingPos = 0;
        }
        return _pending.Length == 0 ? 0f : _pending[_pendingPos++];
    }
}
