namespace SyntheticMind.Audio;

/// <summary>
/// Cuts a continuous audio stream into word-like segments — the first step toward words instead of
/// whole clips (finding 037). It does the one thing that makes word-finding tractable in THIS corpus:
/// child-directed speech is spoken slowly, with exaggerated pauses around isolated words ("bus… bus!").
/// So a simple energy VAD — a voiced run bounded by pauses, of plausible word length — carves out
/// word-sized chunks surprisingly well. Each completed segment yields its mean mel (its spectral
/// signature) for clustering into word-units.
///
/// Fed one hop at a time. Returns null on most hops; when a voiced run of word-ish length just ended,
/// returns that word's mean mel. Deliberately dumb and fixed, like the cochlea it sits behind.
/// </summary>
public sealed class WordSegmenter
{
    private readonly int _minHops, _maxHops, _hangHops;
    private readonly float _activate, _floorRate;

    private float _floor;                 // running noise floor (quiet energy)
    private bool _floorInit;              // seed the floor from the first hop, not a guessed constant
    private float[] _acc;                 // mel accumulator over the current voiced run
    private int _voiced;                  // hops of voice in the current run
    private int _silence;                 // trailing silence hops since voice
    private bool _inWord;

    /// <param name="minHops">Shortest run that counts as a word (drop blips).</param>
    /// <param name="maxHops">Longest run still treated as one word (a longer run is continuous speech,
    /// not an isolated word — emitted split at this length rather than swallowing a whole sentence).</param>
    /// <param name="hangHops">Trailing quiet hops that close a word (ride over tiny gaps within it).</param>
    /// <param name="activateOverFloor">Energy must exceed floor × this to count as voice.</param>
    public WordSegmenter(int melBands, int minHops = 12, int maxHops = 90, int hangHops = 6,
        float activateOverFloor = 4f, float floorRate = 0.01f)
    {
        _minHops = minHops;
        _maxHops = maxHops;
        _hangHops = hangHops;
        _activate = activateOverFloor;
        _floorRate = floorRate;
        _acc = new float[melBands];
    }

    /// <summary>Feed one hop (its mel and its short-time energy). Returns the completed word's mean
    /// mel when a voiced run just ended at word length, else null.</summary>
    public float[]? Accept(float[] mel, float energy)
    {
        if (!_floorInit) { _floor = MathF.Max(energy, 1e-6f); _floorInit = true; }   // seed from ambient
        if (energy < _floor) _floor = energy;                    // snap down to any new quiet minimum
        var voiced = energy > _floor * _activate;
        if (!voiced) _floor += _floorRate * (energy - _floor);   // adapt the floor up slowly in quiet

        if (voiced)
        {
            for (var i = 0; i < _acc.Length; i++) _acc[i] += mel[i];
            _voiced++;
            _silence = 0;
            _inWord = true;
            if (_voiced >= _maxHops) return Emit();   // too long for one word → cut here
            return null;
        }

        if (_inWord && ++_silence >= _hangHops) return Emit();   // pause long enough → word ended
        return null;
    }

    /// <summary>Flush any word in progress (e.g. at end of stream).</summary>
    public float[]? Flush() => _inWord ? Emit() : null;

    private float[]? Emit()
    {
        float[]? result = null;
        if (_voiced >= _minHops)
        {
            result = new float[_acc.Length];
            for (var i = 0; i < _acc.Length; i++) result[i] = _acc[i] / _voiced;
        }
        Array.Clear(_acc);
        _voiced = 0;
        _silence = 0;
        _inWord = false;
        return result;   // null if the run was too short to be a word
    }
}
