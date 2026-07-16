using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// The collapse detector is the load-bearing instrument of the whole project (SCAFFOLD.md §7):
/// a collapsed model and a perfect model are indistinguishable from loss alone. If this class is
/// wrong, every "it works" is unfalsifiable. So it gets tested against cases with known answers.
/// </summary>
public class CollapseMonitorTests
{
    [Fact]
    public void A_constant_state_reads_as_collapsed()
    {
        var monitor = new CollapseMonitor(4);
        for (var i = 0; i < 100; i++) monitor.Record([0.5f, 0.5f, 0.5f, 0.5f]);

        var report = monitor.Report();
        Assert.True(report.Collapsed);
        Assert.True(report.MeanVariance < 1e-6f);
    }

    [Fact]
    public void State_spread_evenly_across_dimensions_reads_as_full_rank()
    {
        var monitor = new CollapseMonitor(4);
        var random = new Random(1);

        // Every dimension varies independently → participation ratio should approach the width.
        for (var i = 0; i < 2000; i++)
            monitor.Record([
                (float)random.NextDouble(), (float)random.NextDouble(),
                (float)random.NextDouble(), (float)random.NextDouble()]);

        var report = monitor.Report();
        Assert.False(report.Collapsed);
        Assert.True(report.ParticipationRatio > 3.5f, $"expected near-4, got {report.ParticipationRatio}");
    }

    [Fact]
    public void State_confined_to_one_direction_reads_as_rank_one()
    {
        var monitor = new CollapseMonitor(4);
        var random = new Random(2);

        // All four dimensions are the SAME scalar times a fixed vector: healthy per-dimension
        // variance, but only one real degree of freedom. This is the case per-dimension variance
        // alone would miss — the reason the monitor computes the full covariance.
        for (var i = 0; i < 2000; i++)
        {
            var t = (float)random.NextDouble();
            monitor.Record([t, 2 * t, -t, 0.5f * t]);
        }

        var report = monitor.Report();
        Assert.True(report.ParticipationRatio < 1.5f, $"expected near-1, got {report.ParticipationRatio}");
        Assert.True(report.Collapsed);
    }

    [Fact]
    public void Reports_harmlessly_before_it_has_data()
    {
        var report = new CollapseMonitor(4).Report();
        Assert.Equal(4, report.Width);
    }
}
