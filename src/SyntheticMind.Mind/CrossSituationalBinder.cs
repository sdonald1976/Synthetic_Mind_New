namespace SyntheticMind.Mind;

/// <summary>
/// Binds across senses when co-occurrence is MESSY — the cross-situational learning that
/// <see cref="CrossModalStore"/> can't do. Where that store binds a clean (heard, seen) pair, this
/// handles the real case: many things present at once, so no single episode says which sound goes
/// with which sight. The true pairing is only recoverable statistically — it's the one that stays
/// consistent across episodes (Yu &amp; Smith; the leading account of how infants learn words).
///
/// Works on discovered UNIT IDS (from clustering the representations — findings 016/017), not raw
/// vectors. Each episode it's told which sound-units and which sight-units were present (unpaired);
/// it accumulates co-occurrence, and recovers each sound's referent as the sight with the highest
/// pointwise mutual information — which normalizes out how common each unit is, so a weak-but-
/// consistent pairing beats a frequent-but-incidental one (finding 022: robust down to ~30%
/// co-occurrence).
/// </summary>
public sealed class CrossSituationalBinder
{
    private readonly Dictionary<(int Heard, int Seen), int> _joint = new();
    private readonly Dictionary<int, int> _heard = new();
    private readonly Dictionary<int, int> _seen = new();
    private int _episodes;

    /// <summary>One episode: the sound-units and sight-units present together, pairing unknown.</summary>
    public void Observe(IReadOnlyCollection<int> heardUnits, IReadOnlyCollection<int> seenUnits)
    {
        _episodes++;
        foreach (var h in heardUnits) _heard[h] = _heard.GetValueOrDefault(h) + 1;
        foreach (var s in seenUnits) _seen[s] = _seen.GetValueOrDefault(s) + 1;
        foreach (var h in heardUnits)
            foreach (var s in seenUnits)
                _joint[(h, s)] = _joint.GetValueOrDefault((h, s)) + 1;
    }

    /// <summary>The seen unit most likely to be what this sound refers to (highest PMI), or null.</summary>
    public int? ReferentOf(int heardUnit)
    {
        if (_episodes == 0 || !_heard.ContainsKey(heardUnit)) return null;

        int? best = null;
        var bestPmi = double.NegativeInfinity;
        foreach (var (seen, seenCount) in _seen)
        {
            if (!_joint.TryGetValue((heardUnit, seen), out var jointCount) || jointCount == 0) continue;
            // PMI = log( P(h,s) / (P(h)·P(s)) ). P(h) is constant across candidates, so it doesn't
            // change the argmax — but computed in full so the value is a usable confidence.
            var pJoint = (double)jointCount / _episodes;
            var pHeard = (double)_heard[heardUnit] / _episodes;
            var pSeen = (double)seenCount / _episodes;
            var pmi = Math.Log(pJoint / (pHeard * pSeen));
            if (pmi > bestPmi) { bestPmi = pmi; best = seen; }
        }
        return best;
    }
}
