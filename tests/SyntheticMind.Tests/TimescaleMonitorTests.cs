using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// The timescale monitor is the instrument that makes "abstraction" a measurable claim rather
/// than a vibe (finding 004). If it can't tell a slow signal from a fast one on cases with known
/// answers, its verdicts on the hierarchy mean nothing.
/// </summary>
public class TimescaleMonitorTests
{
    [Fact]
    public void Independent_noise_reads_as_fast()
    {
        // Each tick unrelated to the last → lag-1 autocorrelation ~0.
        var monitor = new TimescaleMonitor(4);
        var random = new Random(1);
        for (var i = 0; i < 5000; i++)
            monitor.Record([
                (float)random.NextDouble(), (float)random.NextDouble(),
                (float)random.NextDouble(), (float)random.NextDouble()]);

        Assert.True(monitor.Report().Persistence < 0.2f, $"noise should read fast, got {monitor.Report()}");
    }

    [Fact]
    public void A_slowly_turning_signal_reads_as_slow()
    {
        // A smooth sinusoid barely moves tick to tick → persistence near 1.
        var monitor = new TimescaleMonitor(4);
        for (var t = 0; t < 5000; t++)
        {
            var a = t * 0.02f;
            monitor.Record([MathF.Sin(a), MathF.Cos(a), MathF.Sin(a * 0.5f), MathF.Cos(a * 0.5f)]);
        }

        Assert.True(monitor.Report().Persistence > 0.9f, $"a smooth signal should read slow, got {monitor.Report()}");
    }

    [Fact]
    public void The_slow_signal_reads_as_slower_than_the_fast_one()
    {
        // The comparison finding 004 actually depends on: can the instrument rank two signals?
        var fast = new TimescaleMonitor(1);
        var slow = new TimescaleMonitor(1);
        var random = new Random(2);
        var walk = 0f;

        for (var t = 0; t < 5000; t++)
        {
            fast.Record([(float)random.NextDouble()]);
            walk += ((float)random.NextDouble() - 0.5f) * 0.02f;   // slow drift
            slow.Record([walk]);
        }

        Assert.True(slow.Report().Persistence > fast.Report().Persistence + 0.25f,
            $"slow {slow.Report()} should clearly beat fast {fast.Report()}");
    }

    [Fact]
    public void Reports_harmlessly_before_it_has_data()
    {
        Assert.Equal(0f, new TimescaleMonitor(4).Report().Persistence);
    }
}
