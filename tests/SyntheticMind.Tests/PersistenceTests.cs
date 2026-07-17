using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 026 — learned grounding survives a restart. Both stores round-trip through disk with
/// recall intact, so a session's teaching isn't lost on exit.
/// </summary>
public class PersistenceTests
{
    [Fact]
    public void CrossModalStore_round_trips_and_still_recalls()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sm-xm-{Guid.NewGuid():N}.json");
        try
        {
            var store = new CrossModalStore();
            var soundA = new[] { 1f, 0f, 0f }; var sightA = new[] { 0f, 1f, 0f };
            var soundB = new[] { 0f, 0f, 1f }; var sightB = new[] { 1f, 0f, 0f };
            store.Bind(soundA, sightA);
            store.Bind(soundB, sightB);
            store.Save(path);

            var reloaded = CrossModalStore.Load(path);
            Assert.Equal(store.Count, reloaded.Count);

            // The A-sound should still recall the concept whose sight is sightA, across the restart.
            Assert.Equal(reloaded.NearestByAudio(soundA), reloaded.NearestByVisual(sightA));
            Assert.Equal(reloaded.NearestByAudio(soundB), reloaded.NearestByVisual(sightB));
            Assert.NotEqual(reloaded.NearestByAudio(soundA), reloaded.NearestByAudio(soundB));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ConceptStore_round_trips_and_still_recognizes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sm-cs-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ConceptStore();
            store.Teach("circle", [1f, 0f, 0f]);
            store.Teach("arrow", [0f, 1f, 0f]);
            store.Save(path);

            var reloaded = ConceptStore.Load(path);
            Assert.Equal(2, reloaded.Names.Count);
            Assert.Equal("circle", reloaded.Recall([0.9f, 0.1f, 0f])!.Value.Name);
            Assert.Equal("arrow", reloaded.Recall([0.1f, 0.9f, 0f])!.Value.Name);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoadOrNew_starts_empty_when_there_is_no_file()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"sm-none-{Guid.NewGuid():N}.json");
        Assert.Equal(0, CrossModalStore.LoadOrNew(missing).Count);
        Assert.Empty(ConceptStore.LoadOrNew(missing).Names);
    }
}
