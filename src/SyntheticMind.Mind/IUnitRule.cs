namespace SyntheticMind.Mind;

/// <summary>
/// The swappable interior of a unit — SCAFFOLD.md decisions 1, 2 and 5 all live behind this.
///
/// Note what this interface CANNOT express: there is no way to hand a rule anything from
/// another unit, or from a loss computed elsewhere, or from the future. It sees its own input,
/// its own context from above, and its own error. That's it. **Locality is enforced by the
/// type, not by discipline** — if a rule could cheat, someone eventually would.
///
/// This interface exists at its first implementation, which breaks the rule I set in the CLI
/// work. It earns the exception: the entire project is "try several learning rules and see
/// which one works." The second implementation isn't speculative, it's the point.
/// </summary>
public interface IUnitRule
{
    string Name { get; }

    int InputWidth { get; }

    /// <summary>Width of the state this unit publishes upward.</summary>
    int StateWidth { get; }

    /// <summary>Fold arriving input into state. Returns the new state.</summary>
    float[] Observe(float[] input, float[]? context);

    /// <summary>Predict the NEXT input, from the state as it stands now.</summary>
    float[] Predict(float[]? context);

    /// <summary>
    /// Learn from how wrong the last prediction was. Local only — whatever the rule needs
    /// to attribute this error, it must already be holding.
    /// </summary>
    void Learn(float[] predictionError);
}
