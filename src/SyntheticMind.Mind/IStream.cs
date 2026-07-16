namespace SyntheticMind.Mind;

/// <summary>
/// A source of vectors arriving in order. This is the only input format the system has —
/// video, audio, and text all become this before anything sees them (SCAFFOLD.md §4).
///
/// There is no batch and no epoch. There is a stream and a clock.
/// </summary>
public interface IStream
{
    /// <summary>Human-readable name, for experiment output.</summary>
    string Name { get; }

    /// <summary>Width of each vector the stream emits.</summary>
    int Width { get; }

    /// <summary>Advance one tick and return what arrived.</summary>
    float[] Next();

    /// <summary>Rewind to the beginning. Same seed, same stream — experiments must be repeatable.</summary>
    void Reset();
}
