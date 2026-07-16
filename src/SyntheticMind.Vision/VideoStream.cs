using SyntheticMind.Mind;

namespace SyntheticMind.Vision;

/// <summary>
/// Streams frames from a video file through the <see cref="Retina"/>, producing one feature vector
/// per frame — the perception branch's output, ready for a hierarchy, exactly like AudioStream.
///
/// Reads animated GIF (via our own <see cref="GifDecoder"/> — no dependency, no native codec) — a
/// real, downloadable "video file" in the same spirit as feeding WAVs. All frames are decoded to
/// grayscale up front, then streamed (and looped) on demand.
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

        var (frames, width, height) = GifDecoder.DecodeGrayscale(path);
        _width = width;
        _height = height;

        foreach (var bytes in frames)
        {
            var pixels = new float[bytes.Length];
            for (var i = 0; i < bytes.Length; i++) pixels[i] = bytes[i] / 255f;
            _frames.Add(pixels);
        }
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
