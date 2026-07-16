using SyntheticMind.Vision;

namespace SyntheticMind.Tests;

public class GifDecoderTests
{
    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Decodes_the_earth_gif_to_full_frames()
    {
        var path = FindRepoFile(Path.Combine("testvideo", "earth.gif"));
        Assert.True(path is not null, "testvideo/earth.gif should be in the repo");

        var (frames, w, h) = GifDecoder.DecodeGrayscale(path!);

        Assert.Equal(400, w);
        Assert.Equal(400, h);
        Assert.True(frames.Count > 30, $"expected the full animation, got {frames.Count} frames");
        Assert.All(frames, f => Assert.Equal(w * h, f.Length));

        // The rotating Earth is a bright disc on black: some pixels bright, plenty dark. Not uniform.
        var frame = frames[frames.Count / 2];
        Assert.Contains(frame, b => b > 100);
        Assert.Contains(frame, b => b < 20);
    }
}
