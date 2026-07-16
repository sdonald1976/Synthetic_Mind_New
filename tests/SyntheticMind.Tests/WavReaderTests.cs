using SyntheticMind.Audio;

namespace SyntheticMind.Tests;

public class WavReaderTests
{
    [Fact]
    public void Reads_a_16bit_mono_wav_back_to_its_samples()
    {
        short[] pcm = [0, 16384, -16384, 32767, -32768];
        var path = WriteWav(pcm, sampleRate: 16000);
        try
        {
            var (samples, rate) = WavReader.ReadMono(path);
            Assert.Equal(16000, rate);
            Assert.Equal(pcm.Length, samples.Length);
            for (var i = 0; i < pcm.Length; i++)
                Assert.Equal(pcm[i] / 32768f, samples[i], 0.0001f);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Skips_unknown_chunks_like_LIST()
    {
        // Real files (e.g. whisper's jfk.wav) carry a LIST/INFO chunk before data. The reader must
        // walk past it, not choke on it.
        var path = WriteWav([100, 200, 300], sampleRate: 8000, withListChunk: true);
        try
        {
            var (samples, rate) = WavReader.ReadMono(path);
            Assert.Equal(8000, rate);
            Assert.Equal(3, samples.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Resampling_changes_length_by_the_rate_ratio()
    {
        var input = new float[1000];
        var output = WavReader.Resample(input, fromRate: 16000, toRate: 8000);
        Assert.Equal(500, output.Length);
    }

    private static string WriteWav(short[] samples, int sampleRate, bool withListChunk = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}.wav");
        using var writer = new BinaryWriter(File.Create(path));
        var dataBytes = samples.Length * 2;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes + (withListChunk ? 12 : 0));
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);              // PCM
        writer.Write((short)1);              // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);        // byte rate
        writer.Write((short)2);              // block align
        writer.Write((short)16);             // bits

        if (withListChunk)
        {
            writer.Write("LIST"u8.ToArray());
            writer.Write(4);
            writer.Write("INFO"u8.ToArray());
        }

        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);
        foreach (var s in samples) writer.Write(s);
        return path;
    }
}
