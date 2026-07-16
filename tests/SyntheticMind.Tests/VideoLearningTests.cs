using SyntheticMind.Mind;
using SyntheticMind.Vision;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 019 — the vision front-end plugs into the same hierarchy as audio. Watches a real video
/// file (rotating Earth GIF) and confirms the model learns its motion: surprise falls with exposure,
/// exactly as it did for speech (finding 014), from the same code with only the front end swapped.
/// </summary>
public class VideoLearningTests
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
    public void Watches_a_video_and_learns_its_motion()
    {
        var path = FindRepoFile(Path.Combine("testvideo", "earth.gif"));
        Assert.True(path is not null, "testvideo/earth.gif should be in the repo");

        var retina = new Retina(grid: 8, motion: true);
        var video = new VideoStream(path!, retina);
        Assert.True(video.FrameCount > 1, "the clip should decode to multiple frames");
        Assert.Equal(retina.Width, video.Width);

        var level0 = new Unit(new LearnedPredictiveRule(video.Width, stateWidth: 12, history: 6, quadraticFeatures: 0));

        float FirstLoop() { var s = 0f; for (var f = 0; f < video.FrameCount; f++) s += level0.Observe(video.Next()).SquaredError; return s / video.FrameCount; }

        var early = FirstLoop();
        float late = early;
        for (var loop = 0; loop < 7; loop++) late = FirstLoop();

        Assert.True(late < 0.75f * early, $"surprise should fall as it learns the motion: {early:F4} → {late:F4}");
    }
}
