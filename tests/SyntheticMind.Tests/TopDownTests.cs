using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 027 — the top-down loop. Information flows DOWN, not just up: a higher level that holds
/// a slow fact hands it to the lower level, which then predicts something beyond its own window.
///
/// The task is a long-range dependency the fast level structurally cannot solve: each block is
/// [cue, 0,0,0,0,0,0,0, cue] — the final value echoes a cue from 9 steps back, past level 0's
/// 4-frame history. Only a level with longer memory, feeding context down, can help.
/// </summary>
public class TopDownTests
{
    private const int BlockLen = 10;

    private static (List<float> Seq, List<bool> IsTarget) MakeBlocks(int count, int seed)
    {
        var rng = new Random(seed);
        var seq = new List<float>();
        var isTarget = new List<bool>();
        for (var b = 0; b < count; b++)
        {
            var cue = rng.Next(2) == 0 ? -1f : 1f;
            for (var pos = 0; pos < BlockLen; pos++)
            {
                seq.Add(pos is 0 || pos == BlockLen - 1 ? cue : 0f);
                isTarget.Add(pos == BlockLen - 1);
            }
        }
        return (seq, isTarget);
    }

    [Fact]
    public void Top_down_context_predicts_across_a_gap_the_fast_level_cannot_see()
    {
        var (seq, isTarget) = MakeBlocks(6000, seed: 1);
        var warm = seq.Count / 2;

        // Baseline: level 0 alone — its 4-frame window never contains the cue, so at the target it
        // can only guess the mean.
        float BaselineTargetError()
        {
            var l0 = new Unit(new LinearDeltaRule(1, historyLength: 4));
            float e = 0; var c = 0;
            for (var i = 0; i < seq.Count; i++)
            {
                var sq = l0.Observe([seq[i]]).SquaredError;
                if (i >= warm && isTarget[i]) { e += sq; c++; }
            }
            return e / c;
        }

        // The loop: a 2-level hierarchy. Level 1 has the memory to hold the cue; the Hierarchy feeds
        // its state down as level 0's context, and level 0 (now context-aware) uses it.
        float LoopTargetError()
        {
            var l0 = new LinearDeltaRule(1, historyLength: 4);
            var l1 = new LearnedPredictiveRule(l0.StateWidth, stateWidth: 4, drive: EncoderDrive.Variance, history: 16, quadraticFeatures: 32);
            var hierarchy = new Hierarchy(l0, l1);
            float e = 0; var c = 0;
            for (var i = 0; i < seq.Count; i++)
            {
                var ticks = hierarchy.Observe([seq[i]]);
                if (i >= warm && isTarget[i]) { e += ticks[0].SquaredError; c++; }
            }
            return e / c;
        }

        var baseline = BaselineTargetError();
        var withLoop = LoopTargetError();

        // Top-down feedback should clearly cut the error the fast level can't reduce on its own.
        Assert.True(withLoop < 0.85f * baseline,
            $"top-down should reduce target error: baseline {baseline:F3} -> loop {withLoop:F3}");
    }
}
