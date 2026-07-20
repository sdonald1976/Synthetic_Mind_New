using System.Numerics.Tensors;
using System.Text.Json;

namespace SyntheticMind.Mind;

/// <summary>
/// Online vector quantizer with a <em>bounded</em> codebook — turns a stream of messy, real-valued
/// perception summaries into a small, stable set of discrete unit ids. This is the consolidation
/// step the watcher was missing: without a cap, every slightly-different real event became its own
/// "concept" (the over-splitting we watched happen on 39 videos — ~1,200 concepts after 5 clips,
/// heading for ten thousand). With a bounded codebook it can only ever mint <c>capacity</c>
/// prototypes; once full, a novel vector snaps to (and nudges) the nearest existing unit instead of
/// spawning a new slot. Real recurring things therefore land on the same id again and again.
///
/// Prototypes are cosine-nearest and kept unit-length; each assignment moves its prototype a little
/// toward the vector (running mean, then renormalize). Deterministic, online, no training pass —
/// same spirit as the fast store / ART used elsewhere, but with a hard ceiling.
/// </summary>
public sealed class VectorQuantizer
{
    private readonly List<float[]> _prototypes = [];
    private readonly List<int> _counts = [];
    private readonly int _capacity;
    private readonly float _newUnitThreshold;
    private readonly bool _subtractRunningMean;
    private float[]? _mean;                 // EMA of inputs, when centering is on (lazily sized)
    private const float MeanRate = 0.02f;   // ~50-sample horizon for the running mean

    /// <param name="capacity">Hard ceiling on the number of units — the codebook can never grow past
    /// this, which is what bounds the concept count.</param>
    /// <param name="newUnitThreshold">Cosine distance (1 − cosine similarity) beyond which a vector
    /// starts a new unit — but only while there is still room under <paramref name="capacity"/>.</param>
    /// <param name="subtractRunningMean">Match on each input's DEVIATION from a running mean rather
    /// than the raw vector. Essential for a low-variance sense (finding 030): a talking-head video is
    /// nearly the same frame every time, so raw vectors all point the same way and collapse onto one
    /// unit; subtracting the typical frame leaves what actually differs, so distinct scenes separate.</param>
    public VectorQuantizer(int capacity = 64, float newUnitThreshold = 0.25f, bool subtractRunningMean = false)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "Codebook needs room for at least one unit.");
        _capacity = capacity;
        _newUnitThreshold = newUnitThreshold;
        _subtractRunningMean = subtractRunningMean;
    }

    /// <summary>How many units the codebook currently holds (≤ capacity).</summary>
    public int Count => _prototypes.Count;

    /// <summary>The hard ceiling on unit count.</summary>
    public int Capacity => _capacity;

    /// <summary>How many vectors have been assigned to a given unit.</summary>
    public int CountOf(int unit) => _counts[unit];

    /// <summary>Map a vector to a unit id, learning online. Mints a new unit only if the vector is
    /// far from every prototype AND there is still room; otherwise merges into the nearest.</summary>
    public int Quantize(float[] v)
    {
        var x = Normalized(Center(v, update: true));
        var (best, bestDist) = Nearest(x);

        if ((best < 0 || bestDist > _newUnitThreshold) && _prototypes.Count < _capacity)
        {
            _prototypes.Add(x);
            _counts.Add(1);
            return _prototypes.Count - 1;
        }

        // Codebook full, or close enough: fold into the nearest prototype (running mean, renormalize).
        var p = _prototypes[best];
        var n = _counts[best];
        for (var j = 0; j < p.Length; j++) p[j] = (p[j] * n + x[j]) / (n + 1);
        Renormalize(p);
        _counts[best] = n + 1;
        return best;
    }

    /// <summary>Map a vector to its nearest unit WITHOUT learning — read-only lookup. −1 if empty.</summary>
    public int Classify(float[] v) => _prototypes.Count == 0 ? -1 : Nearest(Normalized(Center(v, update: false))).Index;

    /// <summary>Optionally re-express a vector as its deviation from the running input mean, updating
    /// that mean if asked. A no-op unless centering is enabled.</summary>
    private float[] Center(float[] v, bool update)
    {
        if (!_subtractRunningMean) return v;
        _mean ??= new float[v.Length];
        var dev = new float[v.Length];
        for (var i = 0; i < v.Length; i++) dev[i] = v[i] - _mean[i];
        if (update) for (var i = 0; i < v.Length; i++) _mean[i] += MeanRate * (v[i] - _mean[i]);
        return dev;
    }

    private (int Index, float Dist) Nearest(float[] x)
    {
        var best = -1;
        var bestDist = float.MaxValue;
        for (var i = 0; i < _prototypes.Count; i++)
        {
            var d = 1f - TensorPrimitives.Dot<float>(x, _prototypes[i]); // both unit-length → cosine distance
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return (best, bestDist);
    }

    // --- persistence: the codebook survives a restart, so unit ids stay meaningful across runs ----

    private sealed record Persisted(int Capacity, float NewUnitThreshold, bool SubtractRunningMean,
                                    float[]? Mean, List<float[]> Prototypes, List<int> Counts);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var dto = new Persisted(_capacity, _newUnitThreshold, _subtractRunningMean, _mean, _prototypes, _counts);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    public static VectorQuantizer Load(string path)
    {
        var dto = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(path))
                  ?? throw new InvalidDataException($"Could not read codebook from '{path}'.");
        var vq = new VectorQuantizer(dto.Capacity, dto.NewUnitThreshold, dto.SubtractRunningMean) { _mean = dto.Mean };
        vq._prototypes.AddRange(dto.Prototypes);
        vq._counts.AddRange(dto.Counts);
        return vq;
    }

    public static VectorQuantizer LoadOrNew(string path, int capacity = 64, float newUnitThreshold = 0.25f, bool subtractRunningMean = false)
        => File.Exists(path) ? Load(path) : new VectorQuantizer(capacity, newUnitThreshold, subtractRunningMean);

    private static void Renormalize(float[] v)
    {
        var norm = TensorPrimitives.Norm<float>(v);
        if (norm == 0 || !float.IsFinite(norm)) return;
        TensorPrimitives.Divide<float>(v, norm, v);
    }

    private static float[] Normalized(float[] v)
    {
        var norm = TensorPrimitives.Norm<float>(v);
        var result = new float[v.Length];
        if (norm == 0 || !float.IsFinite(norm))
        {
            // Degenerate input (a silent hop, a blank frame): map to one fixed direction so it always
            // consolidates onto a single "nothing" unit and never crashes an unattended batch run.
            Array.Fill(result, 1f / MathF.Sqrt(v.Length));
            return result;
        }
        TensorPrimitives.Divide<float>(v, norm, result);
        return result;
    }
}
