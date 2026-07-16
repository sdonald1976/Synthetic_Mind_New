using System.Numerics.Tensors;

namespace SyntheticMind.Mind;

/// <summary>What the store believes it's looking at. Margin is the gap to the runner-up — a thin
/// margin means two concepts look alike, not that this one is wrong.</summary>
public readonly record struct Recognition(string Name, float Similarity, float Margin);

/// <summary>
/// Binds a perceptual representation to a name — the Concept System from [ARCHITECTURE.md](../../ARCHITECTURE.md),
/// and Slice 0's "teach it a thing, ask what it is", finally built on the real pipeline.
///
/// Grounding is a different operation from the predictive hierarchy: the hierarchy *learns to
/// predict* a stream; this *binds* a representation to a symbol. They're complementary — the
/// perception branch produces the representation, this stores what it co-refers to. It deliberately
/// grounds the PERCEPTION (findings 008/015/016: categorical structure lives there, not in the
/// learned encoder).
///
/// Learning is one-shot and cannot forget by construction: each name is a running-mean prototype of
/// the examples taught for it, and names don't share storage. Recognition is nearest prototype by
/// cosine similarity, with the margin to the runner-up carried through — exactly Slice 0's design.
/// </summary>
public sealed class ConceptStore
{
    private readonly Dictionary<string, float[]> _prototypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => _prototypes.Keys;

    /// <summary>Bind one example of <paramref name="name"/> to its representation. Repeated calls
    /// refine the prototype (running mean); the first already works.</summary>
    public void Teach(string name, float[] representation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var r = Normalized(representation);

        if (_prototypes.TryGetValue(name, out var proto))
        {
            var c = _counts[name];
            for (var i = 0; i < proto.Length; i++) proto[i] = (proto[i] * c + r[i]) / (c + 1);
            _counts[name] = c + 1;
        }
        else
        {
            _prototypes[name] = r;
            _counts[name] = 1;
        }
    }

    /// <summary>Nearest concept to a representation, or null if nothing's been taught.</summary>
    public Recognition? Recall(float[] representation)
    {
        if (_prototypes.Count == 0) return null;
        var r = Normalized(representation);

        string? best = null;
        float bestSim = float.NegativeInfinity, secondSim = float.NegativeInfinity;
        foreach (var (name, proto) in _prototypes)
        {
            var sim = TensorPrimitives.CosineSimilarity<float>(r, proto);
            if (sim > bestSim) { secondSim = bestSim; bestSim = sim; best = name; }
            else if (sim > secondSim) secondSim = sim;
        }

        var margin = float.IsNegativeInfinity(secondSim) ? bestSim : bestSim - secondSim;
        return new Recognition(best!, bestSim, margin);
    }

    public bool Forget(string name) => _prototypes.Remove(name) & _counts.Remove(name);

    private static float[] Normalized(float[] v)
    {
        var norm = TensorPrimitives.Norm<float>(v);
        if (norm == 0 || !float.IsFinite(norm))
            throw new ArgumentException("Representation has zero or non-finite magnitude.", nameof(v));
        var result = new float[v.Length];
        TensorPrimitives.Divide<float>(v, norm, result);
        return result;
    }
}
