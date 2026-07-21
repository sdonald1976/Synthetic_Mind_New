using System.Text.Json;

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

    /// <summary>How many co-occurrence episodes have been observed.</summary>
    public int Episodes => _episodes;

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

    /// <summary>The heard (sound) unit most strongly bound to a given seen (sight) unit — the reverse
    /// of <see cref="ReferentOf"/>, for see→say: perceive a scene, recall the sound that goes with it.
    /// Requires a minimum joint count so a one-off coincidence can't be recalled as "the" sound.</summary>
    public (int Heard, double Pmi, int JointCount)? HeardForSeen(int seenUnit, int minJointCount = 3)
    {
        if (_episodes == 0 || !_seen.TryGetValue(seenUnit, out var seenCount)) return null;

        (int Heard, double Pmi, int JointCount)? best = null;
        var bestPmi = double.NegativeInfinity;
        foreach (var (heard, heardCount) in _heard)
        {
            if (!_joint.TryGetValue((heard, seenUnit), out var jointCount) || jointCount < minJointCount) continue;
            var pmi = Math.Log(((double)jointCount / _episodes) / (((double)heardCount / _episodes) * ((double)seenCount / _episodes)));
            if (pmi > bestPmi) { bestPmi = pmi; best = (heard, pmi, jointCount); }
        }
        return best;
    }

    /// <summary>
    /// The strongest recurring sound↔sight pairings, ranked by PMI. A minimum joint count is required
    /// so a pair that happened to co-occur once (infinite-looking PMI) can't top a pairing that
    /// genuinely recurs across the playlist — support first, then strength.
    /// </summary>
    public IReadOnlyList<(int Heard, int Seen, double Pmi, int JointCount)> TopPairings(int k, int minJointCount = 3)
    {
        if (_episodes == 0) return [];
        var list = new List<(int Heard, int Seen, double Pmi, int JointCount)>();
        foreach (var ((heard, seen), joint) in _joint)
        {
            if (joint < minJointCount) continue;
            var pmi = Math.Log(((double)joint / _episodes) / (((double)_heard[heard] / _episodes) * ((double)_seen[seen] / _episodes)));
            list.Add((heard, seen, pmi, joint));
        }
        return list.OrderByDescending(t => t.Pmi).ThenByDescending(t => t.JointCount).Take(k).ToList();
    }

    // --- persistence: co-occurrence statistics survive a restart --------------------------------

    private sealed record CountEntry(int Unit, int Count);
    private sealed record JointEntry(int Heard, int Seen, int Count);
    private sealed record Persisted(int Episodes, List<JointEntry> Joint, List<CountEntry> Heard, List<CountEntry> Seen);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var dto = new Persisted(
            _episodes,
            _joint.Select(kv => new JointEntry(kv.Key.Heard, kv.Key.Seen, kv.Value)).ToList(),
            _heard.Select(kv => new CountEntry(kv.Key, kv.Value)).ToList(),
            _seen.Select(kv => new CountEntry(kv.Key, kv.Value)).ToList());
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    public static CrossSituationalBinder Load(string path)
    {
        var dto = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(path))
                  ?? throw new InvalidDataException($"Could not read co-occurrence statistics from '{path}'.");
        var b = new CrossSituationalBinder { _episodes = dto.Episodes };
        foreach (var e in dto.Joint) b._joint[(e.Heard, e.Seen)] = e.Count;
        foreach (var e in dto.Heard) b._heard[e.Unit] = e.Count;
        foreach (var e in dto.Seen) b._seen[e.Unit] = e.Count;
        return b;
    }

    public static CrossSituationalBinder LoadOrNew(string path)
        => File.Exists(path) ? Load(path) : new CrossSituationalBinder();
}
