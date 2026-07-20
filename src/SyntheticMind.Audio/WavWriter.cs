using System.Text;

namespace SyntheticMind.Audio;

/// <summary>
/// Writes mono float samples to a 16-bit PCM WAV — the counterpart to <see cref="WavReader"/>, just
/// enough to save a short clip a human can actually listen to (the exemplar dumper uses it to render
/// the audio behind each discovered sound-unit). Samples are clamped to [-1, 1].
/// </summary>
public static class WavWriter
{
    public static void WriteMono(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var dataBytes = samples.Length * 2;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);              // file size minus the first 8 bytes
        w.Write(Encoding.ASCII.GetBytes("WAVE"));

        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                          // PCM fmt chunk size
        w.Write((short)1);                    // format 1 = PCM
        w.Write((short)1);                    // channels = mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);              // byte rate = rate * channels * bytesPerSample
        w.Write((short)2);                    // block align = channels * bytesPerSample
        w.Write((short)16);                   // bits per sample

        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (var s in samples)
        {
            var clamped = Math.Clamp(s, -1f, 1f);
            w.Write((short)Math.Round(clamped * 32767f));
        }
    }
}
