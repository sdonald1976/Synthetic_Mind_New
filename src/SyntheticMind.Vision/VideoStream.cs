using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SyntheticMind.Mind;

namespace SyntheticMind.Vision;

/// <summary>
/// Streams frames from a video file through the <see cref="Retina"/>, producing one feature vector
/// per frame — the perception branch's output, ready for a hierarchy, exactly like AudioStream.
///
/// Reads animated GIF (via ImageSharp, no native codec needed) — a real, downloadable "video file"
/// in the same spirit as feeding WAVs. All frames are decoded to grayscale up front, then streamed
/// (and looped) on demand.
/// </summary>
public sealed class VideoStream : IStream
{
    private readonly List<float[]> _frames = [];
    private readonly int _width;
    private readonly int _height;
    private readonly Retina _retina;
    private int _position;

    public VideoStream(string path, Retina retina)
    {
        _retina = retina;

        using var image = Image.Load<L8>(path);   // L8 = 8-bit luminance (grayscale)
        _width = image.Width;
        _height = image.Height;

        for (var f = 0; f < image.Frames.Count; f++)
        {
            using var frame = image.Frames.CloneFrame(f);
            var bytes = new byte[_width * _height];
            frame.CopyPixelDataTo(bytes);
            var pixels = new float[bytes.Length];
            for (var i = 0; i < bytes.Length; i++) pixels[i] = bytes[i] / 255f;
            _frames.Add(pixels);
        }

        if (_frames.Count == 0) throw new InvalidDataException($"'{path}' decoded to zero frames.");
    }

    public string Name => "video";
    public int Width => _retina.Width;

    /// <summary>Native frame count of the clip (before looping).</summary>
    public int FrameCount => _frames.Count;
    public int FrameWidth => _width;
    public int FrameHeight => _height;

    /// <summary>Next frame's features, looping the clip forever so a hierarchy can keep learning.</summary>
    public float[] Next()
    {
        var frame = _frames[_position % _frames.Count];
        _position++;
        return _retina.Process(frame, _width, _height);
    }

    public void Reset()
    {
        _position = 0;
        _retina.Reset();
    }
}
