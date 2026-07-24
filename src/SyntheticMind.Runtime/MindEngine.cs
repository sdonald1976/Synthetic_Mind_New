using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

namespace SyntheticMind.Runtime;

/// <summary>What the mind is perceiving this instant — the downsampled frame (BGR), the attention
/// window it's looking at, and which object-unit it decided that is. A shell (console or GUI) can
/// render it; the console ignores it, the tuning window draws the box on the frame.</summary>
public readonly record struct Percept(byte[] Bgr, int Width, int Height, int BoxX, int BoxY, int BoxW, int BoxH, int ObjectUnit);

/// <summary>
/// The mind as a reusable engine, lifted out of the console shell so any front-end can drive and
/// watch it. Same heartbeat as before — attend to an object, hear words, bind, remember, speak — but
/// now it raises events (<see cref="Log"/>, <see cref="Perceived"/>) instead of writing to the
/// console, exposes the knobs worth tuning live, and can be Stopped from another thread. The console
/// app and the WinForms tuning window are both thin shells over this.
/// </summary>
public sealed class MindEngine
{
    private const int SampleRate = 16000, Hop = 160, Fft = 512, MelBands = 20, CamW = 120, CamH = 90, FrameStride = 3;
    private const int FoveaGrid = 8, Orientations = 4, TrajKeys = 3, TrajFrames = 8, SaySamples = 3600;
    private const int DispW = 480, DispH = 360;   // preview resolution (the real frame, not the 120×90 the mind sees)

    /// <summary>Inner-life narration (what it hears, binds, says). The console prints these.</summary>
    public event Action<string>? Log;
    /// <summary>What it's seeing right now, ~per frame. The tuning window draws it; the console ignores it.</summary>
    public event Action<Percept>? Perceived;

    // --- knobs worth tuning while it runs (fields the loop reads each tick) ---
    public int SpeakCooldownTicks { get; set; } = 300;
    public int RecallSupport { get; set; } = 4;
    public volatile bool Paused;

    /// <summary>Novelty spike needed to glance away from the person; lower = glances more readily.</summary>
    public float GlanceTrigger { get => _attention.GlanceTrigger; set => _attention.GlanceTrigger = value; }
    /// <summary>0..1: down-weight skin so a non-skin object can beat a face/arm for attention.</summary>
    public float SkinSuppress { get => _attention.SkinSuppress; set => _attention.SkinSuppress = value; }

    public int WordCount => _wordVq.Count;
    public int ObjectCount => _objectVq.Count;
    public int Bindings => _binder.Episodes;
    public long Ticks => _tick;

    private readonly string _stateDir, _saidDir, _objectCbPath, _wordCbPath, _pairingsPath;
    private readonly Cochlea _cochlea;
    private readonly Unit _audioL0;
    private readonly ObjectAttention _attention;
    private VectorQuantizer _wordVq, _objectVq;
    private CrossSituationalBinder _binder;
    private readonly FormantSynth _synth;
    private readonly VocalBabbler _mouth;
    private readonly Dictionary<int, float[]> _soundMemory = new();

    // perception working state
    private readonly float[] _buf = new float[Fft];
    private readonly float[] _hopBuf = new float[Hop];
    private int _hopFill;
    private readonly float[] _ring = new float[SaySamples];
    private int _ringPos, _ringCount;
    private float _audioBaseline = 1e-4f;
    private int _currentObject = -1;
    private WordSegmenter _seg = new(MelBands);
    private long _tick, _spoken, _wordsHeard, _lastSpeakTick = -10000;

    private readonly float[] _luma = new float[CamW * CamH], _red = new float[CamW * CamH], _green = new float[CamW * CamH], _blue = new float[CamW * CamH];
    private readonly byte[] _bgr = new byte[CamW * CamH * 3];
    private readonly Mat _small = new();
    private readonly Mat _disp = new();
    private readonly byte[] _dispBytes = new byte[DispW * DispH * 3];
    private long _lastEmit;
    private volatile bool _alive = true;

    public MindEngine(string stateDir)
    {
        _stateDir = stateDir;
        _saidDir = Path.Combine(stateDir, "said");
        Directory.CreateDirectory(_saidDir);
        _objectCbPath = Path.Combine(stateDir, "object-codebook.json");
        _wordCbPath = Path.Combine(stateDir, "word-codebook.json");
        _pairingsPath = Path.Combine(stateDir, "pairings.json");

        _cochlea = new Cochlea(SampleRate, Fft, MelBands);
        _audioL0 = new Unit(new LearnedPredictiveRule(MelBands, stateWidth: 8, history: 8, quadraticFeatures: 0));
        _attention = new ObjectAttention(new Retina(FoveaGrid, motion: false, orientations: Orientations, color: true), mode: AttentionMode.PersonCentred)
        { SkinSuppress = 0.25f };   // live-tuned default: lets a held-up object beat the face/arm, still rests on the person
        var objectWidth = _attention.Width;

        _wordVq = VectorQuantizer.LoadOrNew(_wordCbPath, capacity: 48, newUnitThreshold: 0.20f);
        _objectVq = VectorQuantizer.LoadOrNew(_objectCbPath, capacity: 64, newUnitThreshold: 0.30f, subtractRunningMean: true);
        if (_objectVq.Count > 0 && _objectVq.Dimension != objectWidth)
        { _wordVq = new VectorQuantizer(48, 0.20f); _objectVq = new VectorQuantizer(64, 0.30f, subtractRunningMean: true); }
        _binder = CrossSituationalBinder.LoadOrNew(_pairingsPath);

        _synth = new FormantSynth(SampleRate);
        _mouth = new VocalBabbler(TrajKeys * _synth.ControlCount, TrajFrames * MelBands,
            c => MelTrajectory(_synth.SynthesizeTrajectory(ToKeys(c), SaySamples)), seed: 1, normalizeMel: true);
        for (var i = 0; i < 600; i++) _mouth.Babble();   // learn its own voice before it opens its eyes
    }

    private void Waking()
    {
        if (_binder.Episodes > 0) Log?.Invoke($"remembering a past life: {_wordVq.Count} words, {_objectVq.Count} objects, {_binder.Episodes} bindings");
    }

    public void Stop() { _alive = false; }

    // --- drivers ----------------------------------------------------------------------------------

    /// <summary>Watch a folder of videos on an endless loop (its "world").</summary>
    public void RunWorld(string folder)
    {
        var exts = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".m4v" };
        string[] Scan() => Directory.Exists(folder)
            ? Directory.GetFiles(folder).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray()
            : [];
        var videos = Scan();
        if (videos.Length == 0) { Log?.Invoke($"no world in {folder}. Point me at a folder of videos, or go live."); return; }

        Waking();
        Log?.Invoke($"awake. watching {videos.Length} video(s) in {folder} on a loop.");
        while (_alive)
        {
            foreach (var video in videos)
            {
                if (!_alive) break;
                var samples = ExtractAudio(video, SampleRate);
                using var cap = new VideoCapture(video);
                if (!cap.IsOpened()) continue;
                _seg = new WordSegmenter(MelBands); _hopFill = 0; _ringCount = 0; _ringPos = 0;
                using var frame = new Mat();
                var samplePos = 0; var frameIndex = 0;

                while (_alive)
                {
                    if (Paused) { Thread.Sleep(30); continue; }
                    var doFrame = frameIndex % FrameStride == 0;
                    var ok = doFrame ? cap.Read(frame) : cap.Grab();
                    if (!ok || (doFrame && frame.Empty())) break;
                    frameIndex++;
                    if (!doFrame) continue;

                    SeeFrame(frame);
                    var target = Math.Min(samples.Length, (int)(cap.Get(VideoCaptureProperties.PosMsec) / 1000.0 * SampleRate));
                    if (target > samplePos) { HearBlock(samples.AsSpan(samplePos, target - samplePos)); samplePos = target; }
                    MaybeSpeak();
                    if (++_tick % 2000 == 0) { Status(); Remember(); }
                }
            }
            videos = Scan();   // re-scan so a still-downloading playlist's new videos get picked up
            Log?.Invoke($"— finished a pass ({videos.Length} video(s) now); going round again.");
        }
        Remember();
    }

    /// <summary>Perceive the real room: webcam (device 0) + microphone.</summary>
    public void RunLive()
    {
        Log?.Invoke("opening webcam...");
        VideoCapture? cam = null;
        for (var dev = 0; dev <= 2; dev++)
        {
            try { cam?.Dispose(); cam = new VideoCapture(dev); } catch (Exception ex) { Log?.Invoke($"  device {dev}: {ex.Message}"); continue; }
            if (cam.IsOpened()) { Log?.Invoke($"webcam opened (device {dev})."); break; }
        }
        if (cam is null || !cam.IsOpened()) { Log?.Invoke("no webcam found (tried device 0 and 1). Is another app using it, or is camera access blocked in Windows privacy settings?"); return; }

        var micQueue = new Queue<float>();
        var micGate = new object();
        WaveInEvent? mic = null;
        try
        {
            mic = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 20 };
            mic.DataAvailable += (_, e) => { lock (micGate) for (var i = 0; i + 1 < e.BytesRecorded; i += 2) micQueue.Enqueue(BitConverter.ToInt16(e.Buffer, i) / 32768f); };
            mic.StartRecording();
        }
        catch (Exception ex) { Log?.Invoke($"(no microphone: {ex.Message} — it will see but not hear words)"); }

        Waking();
        Log?.Invoke("awake. perceiving the real room (webcam + mic).");
        using var frame = new Mat();
        var warmup = 0; var gotFrame = false;
        while (_alive)
        {
            if (Paused) { Thread.Sleep(30); continue; }
            if (!cam.Read(frame) || frame.Empty())
            {
                if (!gotFrame && ++warmup == 300) Log?.Invoke("webcam opened but no frames arriving — likely Windows camera-privacy blocking, or another app holding it.");
                Thread.Sleep(5); continue;
            }
            if (!gotFrame) { gotFrame = true; Log?.Invoke("receiving frames — perceiving the room."); }
            SeeFrame(frame);
            float[] block;
            lock (micGate) { block = micQueue.ToArray(); micQueue.Clear(); }
            if (block.Length > 0) HearBlock(block);
            MaybeSpeak();
            if (++_tick % 500 == 0) { Status(); Remember(); }
        }
        mic?.StopRecording(); mic?.Dispose(); cam.Dispose();
        Remember();
    }

    // --- perception (identical brain, whatever the source) ---------------------------------------

    private void SeeFrame(Mat frame)
    {
        Cv2.Resize(frame, _small, new Size(CamW, CamH));
        Marshal.Copy(_small.Data, _bgr, 0, _bgr.Length);
        for (var i = 0; i < CamW * CamH; i++)
        {
            float b = _bgr[i * 3] / 255f, g = _bgr[i * 3 + 1] / 255f, r = _bgr[i * 3 + 2] / 255f;
            _blue[i] = b; _green[i] = g; _red[i] = r;
            _luma[i] = 0.114f * b + 0.587f * g + 0.299f * r;
        }
        var (objFeat, x0, y0, w, h) = _attention.Attend(_luma, _red, _green, _blue, CamW, CamH);
        _currentObject = _objectVq.Quantize(objFeat);

        // Preview, throttled to ~15 fps: the REAL frame at display size, with the attention window
        // mapped from the mind's 120×90 space onto it.
        var now = Environment.TickCount64;
        if (Perceived is { } handler && now - _lastEmit >= 60)
        {
            _lastEmit = now;
            Cv2.Resize(frame, _disp, new Size(DispW, DispH));
            Marshal.Copy(_disp.Data, _dispBytes, 0, _dispBytes.Length);
            handler(new Percept((byte[])_dispBytes.Clone(), DispW, DispH,
                x0 * DispW / CamW, y0 * DispH / CamH, w * DispW / CamW, h * DispH / CamH, _currentObject));
        }
    }

    private float[] Recent()
    {
        var outv = new float[_ringCount];
        for (var i = 0; i < _ringCount; i++) outv[i] = _ring[(_ringPos - _ringCount + i + SaySamples) % SaySamples];
        return outv;
    }

    private void HearBlock(ReadOnlySpan<float> block)
    {
        foreach (var sample in block)
        {
            _ring[_ringPos] = sample; _ringPos = (_ringPos + 1) % SaySamples; if (_ringCount < SaySamples) _ringCount++;
            _hopBuf[_hopFill++] = sample;
            if (_hopFill < Hop) continue;

            Array.Copy(_buf, Hop, _buf, 0, Fft - Hop);
            Array.Copy(_hopBuf, 0, _buf, Fft - Hop, Hop);
            var energy = 0f;
            for (var i = 0; i < Hop; i++) energy += _hopBuf[i] * _hopBuf[i];
            energy /= Hop;
            _hopFill = 0;

            var mel = _cochlea.Process(_buf);
            var s = _audioL0.Observe(mel).SquaredError;
            _audioBaseline += 0.001f * (s - _audioBaseline);
            var word = _seg.Accept(mel, energy);
            if (word is null) continue;

            var wu = _wordVq.Quantize(word);
            if (_currentObject >= 0) _binder.Observe([wu], [_currentObject]);
            if (_ringCount >= Fft) _soundMemory[wu] = MelTrajectory(Recent());
            _wordsHeard++;
        }
    }

    private void MaybeSpeak()
    {
        if (_currentObject < 0 || _tick - _lastSpeakTick <= SpeakCooldownTicks) return;
        var recalled = _binder.HeardForSeen(_currentObject, minJointCount: RecallSupport);
        if (recalled is not { } r || !_soundMemory.TryGetValue(r.Heard, out var target)) return;
        var (control, _, _) = _mouth.Imitate(target, refineSteps: 80);
        var name = $"utt{_spoken % 500:D3}.wav";   // cycle through 500 files so said/ doesn't grow without bound
        WavWriter.WriteMono(Path.Combine(_saidDir, name), _synth.SynthesizeTrajectory(ToKeys(control), SaySamples), SampleRate);
        Log?.Invoke($"sees object #{_currentObject} — remembers its word (#{r.Heard}, bound {r.JointCount}x) — says it → {name}");
        _lastSpeakTick = _tick; _spoken++;
    }

    private void Status() => Log?.Invoke($"[{_tick}] {_wordVq.Count} words / {_objectVq.Count} objects / {_binder.Episodes} bindings · heard {_wordsHeard} · ear-surprise {_audioBaseline:F3} · said {_spoken}x");
    private void Remember() { _objectVq.Save(_objectCbPath); _wordVq.Save(_wordCbPath); _binder.Save(_pairingsPath); }

    private float[][] ToKeys(float[] c)
    {
        var keys = new float[TrajKeys][];
        for (var k = 0; k < TrajKeys; k++) { keys[k] = new float[_synth.ControlCount]; Array.Copy(c, k * _synth.ControlCount, keys[k], 0, _synth.ControlCount); }
        return keys;
    }

    private float[] MelTrajectory(float[] wave)
    {
        var traj = new float[TrajFrames * MelBands];
        for (var f = 0; f < TrajFrames; f++)
        {
            var s = Math.Clamp((int)((f + 0.5f) / TrajFrames * wave.Length) - Fft / 2, 0, Math.Max(0, wave.Length - Fft));
            var frame = new float[Fft];
            Array.Copy(wave, s, frame, 0, Math.Min(Fft, wave.Length - s));
            var mel = _cochlea.Process(frame);
            Array.Copy(mel, 0, traj, f * MelBands, MelBands);
        }
        return traj;
    }

    private static float[] ExtractAudio(string video, int rate)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}.wav");
        try
        {
            var psi = new ProcessStartInfo("ffmpeg", $"-y -v error -i \"{video}\" -vn -ac 1 -ar {rate} \"{tmp}\"")
            { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            using var p = Process.Start(psi); if (p is null) return [];
            p.WaitForExit();
            return File.Exists(tmp) ? WavReader.ReadMono(tmp, rate) : [];
        }
        catch { return []; }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
