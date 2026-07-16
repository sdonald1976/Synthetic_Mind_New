namespace SyntheticMind.Mind;

/// <summary>
/// A stack of identical units. Level N's input is level N−1's *state*, never its raw signal —
/// that's the JEPA move from SCAFFOLD.md §3, and it's what forces abstraction to appear instead
/// of being designed in.
///
/// Nothing here assigns roles. No level is told it does edges, or objects, or events. If the bet
/// is right, level 2 discovers slower structure than level 1 purely because it's further from
/// the input and its input changes more slowly. If we ever find ourselves hand-tuning what a
/// level represents, the bet is wrong.
///
/// **Context flows down from the previous tick, not this one.** The level above hasn't run yet
/// when the level below needs its context, and reaching forward for it would be leaking the
/// future into the past — the exact bug that makes a model look brilliant and mean nothing.
///
/// Caveat worth stating plainly: context is *plumbed* but nothing consumes it yet. LinearDeltaRule
/// ignores it. What top-down context should actually DO is SCAFFOLD.md decision 5, still open —
/// so this is a wired port with nothing on the other end, on purpose.
/// </summary>
public sealed class Hierarchy
{
    private readonly Unit[] _levels;
    private readonly float[]?[] _previousStates;

    public Hierarchy(params IUnitRule[] rules)
    {
        if (rules.Length == 0) throw new ArgumentException("A hierarchy needs at least one level.", nameof(rules));

        for (var i = 1; i < rules.Length; i++)
            if (rules[i].InputWidth != rules[i - 1].StateWidth)
                throw new ArgumentException(
                    $"Level {i} takes {rules[i].InputWidth}-wide input but level {i - 1} publishes " +
                    $"{rules[i - 1].StateWidth}-wide state.", nameof(rules));

        _levels = [.. rules.Select(r => new Unit(r))];
        _previousStates = new float[]?[rules.Length];
    }

    public int Depth => _levels.Length;
    public Unit this[int level] => _levels[level];

    /// <summary>One tick through every level, bottom to top. Returns each level's result.</summary>
    public Tick[] Observe(float[] input)
    {
        var ticks = new Tick[_levels.Length];
        var signal = input;

        for (var i = 0; i < _levels.Length; i++)
        {
            var context = i + 1 < _levels.Length ? _previousStates[i + 1] : null;
            ticks[i] = _levels[i].Observe(signal, context);
            signal = ticks[i].State;
        }

        for (var i = 0; i < _levels.Length; i++) _previousStates[i] = ticks[i].State;
        return ticks;
    }
}
