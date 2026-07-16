using System.Text;

namespace SyntheticMind.Audio;

/// <summary>
/// Reads a WAV file into mono float samples. Just enough of the format to handle the common cases
/// (16-bit PCM and 32-bit float, any channel count and sample rate), so any random file off the
/// internet can be fed to the cochlea instead of a live microphone.
/// </summary>
public static class WavReader
{
    /// <summary>Read a WAV as mono samples, resampled to <paramref name="targetRate"/>.</summary>
    public static float[] ReadMono(string path, int targetRate)
    {
        var (samples, rate) = ReadMono(path);
        return Resample(samples, rate, targetRate);
    }

    /// <summary>Read a WAV as mono samples at the file's own sample rate.</summary>
    public static (float[] Samples, int SampleRate) ReadMono(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < 12 || Ascii(b, 0) != "RIFF" || Ascii(b, 8) != "WAVE")
            throw new InvalidDataException($"'{path}' is not a RIFF/WAVE file.");

        int channels = 0, sampleRate = 0, bits = 0, format = 0, dataOffset = -1, dataLength = 0;

        var pos = 12;
        while (pos + 8 <= b.Length)
        {
            var id = Ascii(b, pos);
            var size = BitConverter.ToInt32(b, pos + 4);
            var body = pos + 8;

            if (id == "fmt " && body + 16 <= b.Length)
            {
                format = BitConverter.ToUInt16(b, body);
                channels = BitConverter.ToUInt16(b, body + 2);
                sampleRate = BitConverter.ToInt32(b, body + 4);
                bits = BitConverter.ToUInt16(b, body + 14);
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = Math.Min(size, b.Length - body);   // guard truncated files
            }

            if (size <= 0) break;
            pos = body + size + (size & 1);   // chunks are word-aligned
        }

        if (dataOffset < 0 || channels == 0)
            throw new InvalidDataException($"'{path}' has no fmt/data chunks this reader understands.");

        var bytesPerSample = bits / 8;
        var frameSize = bytesPerSample * channels;
        if (frameSize == 0) throw new InvalidDataException("Invalid WAV frame size.");
        var frames = dataLength / frameSize;

        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += ReadSample(b, dataOffset + f * frameSize + c * bytesPerSample, bits, format);
            mono[f] = sum / channels;
        }

        return (mono, sampleRate);
    }

    /// <summary>Linear-interpolation resampling. Good enough to audition a file, not hi-fi.</summary>
    public static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate || input.Length == 0) return input;

        var outLength = (int)((long)input.Length * toRate / fromRate);
        var output = new float[outLength];
        for (var i = 0; i < outLength; i++)
        {
            var srcPos = (double)i * fromRate / toRate;
            var i0 = (int)srcPos;
            var frac = (float)(srcPos - i0);
            var a = input[i0];
            var b = i0 + 1 < input.Length ? input[i0 + 1] : a;
            output[i] = a * (1 - frac) + b * frac;
        }
        return output;
    }

    private static float ReadSample(byte[] b, int offset, int bits, int format)
    {
        // format 3 = IEEE float; 1 = PCM.
        if (format == 3 && bits == 32) return BitConverter.ToSingle(b, offset);
        return bits switch
        {
            16 => BitConverter.ToInt16(b, offset) / 32768f,
            8 => (b[offset] - 128) / 128f,                          // 8-bit WAV is unsigned
            24 => ((b[offset] | (b[offset + 1] << 8) | (SignExtend24(b[offset + 2]) << 16))) / 8388608f,
            32 => BitConverter.ToInt32(b, offset) / 2147483648f,
            _ => throw new NotSupportedException($"{bits}-bit WAV (format {format}) is not supported."),
        };
    }

    private static int SignExtend24(byte high) => (high & 0x80) != 0 ? high | ~0xFF : high;

    private static string Ascii(byte[] b, int offset) => Encoding.ASCII.GetString(b, offset, 4);
}
