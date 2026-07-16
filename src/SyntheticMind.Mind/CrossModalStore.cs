using System.Numerics.Tensors;

namespace SyntheticMind.Mind;

/// <summary>
/// Binds what's heard to what's seen — the Concept System spanning two senses, and the closest the
/// project comes to <em>meaning without a teacher</em> (finding 021). Nothing labels anything: the
/// only signal is co-occurrence in time. When a sound and a sight happen together, this binds their
/// representations, and afterwards either one recalls the other.
///
/// Bindings are novelty-gated prototypes (the fast store / ART again): a co-occurring pair near an
/// existing binding refines it; a novel pair starts a new one. So repeated encounters of the same
/// thing consolidate into one concept holding both its sound signature and its sight signature.
/// Cross-modal recall is nearest-prototype in one modality, returning the other.
/// </summary>
public sealed class CrossModalStore
{
    private sealed class Binding
    {
        public required float[] Audio;
        public required float[] Visual;
        public int Count;
    }

    private readonly List<Binding> _bindings = [];
    private readonly float _vigilance;

    /// <param name="vigilance">How close (joint distance) a pair must be to an existing binding to
    /// merge into it rather than start a new concept.</param>
    public CrossModalStore(float vigilance = 0.35f) => _vigilance = vigilance;

    public int Count => _bindings.Count;

    /// <summary>Bind a co-occurring (heard, seen) pair. No label — being simultaneous is the whole
    /// signal.</summary>
    public void Bind(float[] audio, float[] visual)
    {
        var a = Normalized(audio);
        var v = Normalized(visual);

        var best = -1;
        var bestDist = float.MaxValue;
        for (var i = 0; i < _bindings.Count; i++)
        {
            var d = SquaredDistance(a, _bindings[i].Audio) + SquaredDistance(v, _bindings[i].Visual);
            if (d < bestDist) { bestDist = d; best = i; }
        }

        if (best < 0 || MathF.Sqrt(bestDist) > _vigilance)
        {
            _bindings.Add(new Binding { Audio = a, Visual = v, Count = 1 });
        }
        else
        {
            var b = _bindings[best];
            for (var j = 0; j < a.Length; j++) b.Audio[j] = (b.Audio[j] * b.Count + a[j]) / (b.Count + 1);
            for (var j = 0; j < v.Length; j++) b.Visual[j] = (b.Visual[j] * b.Count + v[j]) / (b.Count + 1);
            b.Count++;
        }
    }

    /// <summary>Hear a sound, recall what it looks like — the visual signature of the nearest binding.</summary>
    public float[]? RecallVisual(float[] audio) => Nearest(audio, b => b.Audio)?.Visual;

    /// <summary>See a sight, recall what it sounds like.</summary>
    public float[]? RecallAudio(float[] visual) => Nearest(visual, b => b.Visual)?.Audio;

    private Binding? Nearest(float[] query, Func<Binding, float[]> key)
    {
        if (_bindings.Count == 0) return null;
        var q = Normalized(query);
        Binding? best = null;
        var bestDist = float.MaxValue;
        foreach (var b in _bindings)
        {
            var d = SquaredDistance(q, key(b));
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }

    private static float SquaredDistance(float[] a, float[] b)
    {
        var d = 0f;
        for (var i = 0; i < a.Length; i++) { var e = a[i] - b[i]; d += e * e; }
        return d;
    }

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
