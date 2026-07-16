using SyntheticMind.Mind;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 022 — cross-modal binding survives messy co-occurrence. Many objects per episode
/// (ambiguity) and a pairing that only holds part of the time (referential noise). The binder must
/// recover the true sound→sight mapping from cross-episode statistics alone.
/// </summary>
public class CrossSituationalTests
{
    private const int Objects = 8;

    private static float Accuracy(int perEpisode, double coherence, int episodes, int seed)
    {
        var rng = new Random(seed);
        var binder = new CrossSituationalBinder();

        for (var e = 0; e < episodes; e++)
        {
            var present = new HashSet<int>();
            while (present.Count < perEpisode) present.Add(rng.Next(Objects));

            var heard = new List<int>();
            var seen = new List<int>();
            foreach (var o in present)
            {
                heard.Add(o);
                // The correct sight accompanies the sound only `coherence` of the time; else a random one.
                seen.Add(rng.NextDouble() < coherence ? o : rng.Next(Objects));
            }
            binder.Observe(heard, seen);
        }

        var correct = 0;
        for (var o = 0; o < Objects; o++) if (binder.ReferentOf(o) == o) correct++;
        return (float)correct / Objects;
    }

    [Fact]
    public void Recovers_pairings_under_ambiguity_and_weak_co_occurrence()
    {
        // 3 objects at once (ambiguous), and the true sight present only half the time (weak).
        // Averaged over seeds so the assertion isn't hostage to one draw.
        var total = 0f;
        for (var seed = 1; seed <= 8; seed++) total += Accuracy(perEpisode: 3, coherence: 0.5, episodes: 500, seed);
        var mean = total / 8;
        Assert.True(mean > 0.85f, $"cross-situational binding should survive mess, got {mean:P0}");
    }

    [Fact]
    public void Perfectly_correlated_distractors_are_the_real_limit()
    {
        // The honest failure mode: two objects that ALWAYS appear together can't be told apart —
        // no statistic can, because their co-occurrence with each other's referent is identical.
        // Here objects 0 and 1 are always co-present; binding for them should be at/near chance,
        // while the independent objects (2..7) are still recovered. Documents the limit as a fact.
        var rng = new Random(1);
        var binder = new CrossSituationalBinder();
        for (var e = 0; e < 1000; e++)
        {
            var present = new HashSet<int> { 0, 1 };          // the glued pair, always together
            while (present.Count < 4) present.Add(rng.Next(Objects));
            var heard = present.ToList();
            var seen = present.ToList();                        // perfect pairing otherwise
            binder.Observe(heard, seen);
        }

        // Independent objects are still recovered...
        var independentOk = 0;
        for (var o = 2; o < Objects; o++) if (binder.ReferentOf(o) == o) independentOk++;
        Assert.True(independentOk >= 5, $"independent objects should still bind, {independentOk}/6");

        // ...but the glued pair {0,1} is genuinely ambiguous: 0's sound co-occurs with sight 0 and
        // sight 1 identically, so the binder cannot reliably prefer the right one.
        var glued0 = binder.ReferentOf(0);
        Assert.True(glued0 is 0 or 1, "a glued pair collapses to an unresolvable set — the known limit");
    }
}
