using NAudio.Wave;
using SyntheticMind.Audio;
using SyntheticMind.Mind;

// Listen to sound → cochlea → hierarchy.
//   dotnet run                       → live microphone
//   dotnet run -- path/to/file.wav   → audition a WAV file and report what the model does
//
// SURPRISE is level 0's prediction error — how wrong its guess of this instant was. It spikes on
// the unexpected (a new sound, an onset) and settles as the model learns to predict what it hears.

const int SampleRate = 16000;
const int Hop = 160;      // 100 mel-frames per second
const int MelBands = 20;

var cochlea = new Cochlea(SampleRate, fftSize: 512, melBands: MelBands);
// No quad features (pitch/timbre are linear in the mel spectrum). Default rate — the encoder is
// scale-free (NLMS, finding 013), so no per-input tuning.
var level0 = new Unit(new LearnedPredictiveRule(MelBands, stateWidth: 8, history: 8, quadraticFeatures: 0));

if (args.Length >= 2 && args[0] == "--segment" && args[1].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
    SegmentFile(args[1], cochlea, level0);
else if (args.Length >= 2 && args[0].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
    AuditionScene(args[0], args[1], cochlea);
else if (args.Length == 1 && args[0].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
    AuditionFile(args[0], cochlea, level0);
else
    LiveMicrophone(cochlea, level0);
return;

// ---- segment mode: where does the unsupervised segmenter fire? ------------------------------
static void SegmentFile(string path, Cochlea cochlea, Unit level0)
{
    var samples = WavReader.ReadMono(path, SampleRate);
    var frames = samples.Length / Hop;
    var pos = 0;
    float[] Pull() { var b = new float[Hop]; for (var i = 0; i < Hop && pos < samples.Length; i++) b[i] = samples[pos++]; return b; }
    var audio = new AudioStream(Pull, cochlea, Hop, normalize: false);

    var surprise = new float[frames];
    var loud = new float[frames];
    for (var t = 0; t < frames; t++)
    {
        var rms = 0f;
        for (var i = 0; i < Hop && t * Hop + i < samples.Length; i++) { var s = samples[t * Hop + i]; rms += s * s; }
        loud[t] = MathF.Sqrt(rms / Hop);
        surprise[t] = level0.Observe(audio.Next()).SquaredError;
    }

    double mean = surprise.Average();
    var sd = Math.Sqrt(surprise.Sum(x => (x - mean) * (x - mean)) / frames);
    var boundaries = new HashSet<int>();
    for (var t = 1; t < frames - 1; t++)
        if (surprise[t] > surprise[t - 1] && surprise[t] >= surprise[t + 1] && surprise[t] > mean + 0.5 * sd) boundaries.Add(t);

    Console.WriteLine($"\n  {Path.GetFileName(path)} — {frames / 100f:F1}s");
    Console.WriteLine($"  {boundaries.Count} boundaries = {boundaries.Count / (frames / 100f):F1}/s (unsupervised — surprise peaks)\n");
    Console.WriteLine("  time   loudness            boundaries");
    const int bucket = 50;
    var maxL = loud.DefaultIfEmpty(1).Max();
    for (var b = 0; b * bucket < frames; b++)
    {
        float l = 0; var nb = 0; var cnt = 0;
        for (var i = b * bucket; i < (b + 1) * bucket && i < frames; i++) { l += loud[i]; if (boundaries.Contains(i)) nb++; cnt++; }
        Console.WriteLine($"  {b * bucket / 100f,4:F1}s  {new string('#', (int)(l / cnt / maxL * 20)),-20}{new string('|', nb)}");
    }
    Console.WriteLine();
}

// ---- scene mode: alternate two files, does a slow level track WHICH is playing? -------------
static void AuditionScene(string pathA, string pathB, Cochlea cochlea)
{
    var a = WavReader.ReadMono(pathA, SampleRate);
    var b = WavReader.ReadMono(pathB, SampleRate);
    Console.WriteLine($"\n  alternating {Path.GetFileName(pathA)} ↔ {Path.GetFileName(pathB)} every 4s\n");

    const int switchSamples = 4 * SampleRate;
    int source = 0, pa = 0, pb = 0; long idx = 0;
    float[] Pull()
    {
        var block = new float[Hop];
        for (var i = 0; i < Hop; i++)
        {
            if (idx > 0 && idx % switchSamples == 0) source = 1 - source;
            block[i] = source == 0 ? a[pa++ % a.Length] : b[pb++ % b.Length];
            idx++;
        }
        return block;
    }
    var audio = new AudioStream(Pull, cochlea, Hop, normalize: false);  // keep the source signal
    // Slow level pools PERCEPTION, not the learned encoder — the encoder discards slow source
    // identity (finding 015). Clock (~2.5s memory) matched to the 4s source switching.
    var slow = new TemporalLevel(MelBands, stride: 10, integratorRate: 0.3f);

    var states = new List<float[]>();
    var sources = new List<float>();
    var frames = (a.Length + b.Length) * 3 / Hop;   // a few loops through both
    for (var t = 0; t < frames; t++)
    {
        var s = slow.Observe(audio.Next());
        if (t < frames / 3) continue;   // warmup
        states.Add(s);
        sources.Add(source);
    }

    Console.WriteLine($"  slow level tracks which source is playing: correlation {MaxAbsCorrelation(states, sources):F3}");
    Console.WriteLine("  (a stable 'what am I hearing' abstraction, formed with no labels)\n");
}

static float MaxAbsCorrelation(List<float[]> states, List<float> target)
{
    var best = 0f;
    for (var d = 0; d < states[0].Length; d++)
    {
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        var n = states.Count;
        for (var i = 0; i < n; i++) { double x = states[i][d], y = target[i]; sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y; }
        var cov = sxy - sx * sy / n; var vx = sxx - sx * sx / n; var vy = syy - sy * sy / n;
        if (vx <= 1e-12 || vy <= 1e-12) continue;
        var r = (float)Math.Abs(cov / Math.Sqrt(vx * vy));
        if (r > best) best = r;
    }
    return best;
}

// ---- WAV file mode: run the clip and show surprise vs. loudness over time --------------------
static void AuditionFile(string path, Cochlea cochlea, Unit level0)
{
    var samples = WavReader.ReadMono(path, SampleRate);
    Console.WriteLine($"\n  {Path.GetFileName(path)} — {samples.Length / (float)SampleRate:F1}s at {SampleRate} Hz\n");

    var pos = 0;
    float[] Pull()
    {
        var block = new float[Hop];
        for (var i = 0; i < Hop && pos < samples.Length; i++) block[i] = samples[pos++];
        return block;
    }
    var audio = new AudioStream(Pull, cochlea, Hop);

    var frames = samples.Length / Hop;
    var surprise = new float[frames];
    var loudness = new float[frames];
    for (var t = 0; t < frames; t++)
    {
        var rms = 0f;
        for (var i = 0; i < Hop && t * Hop + i < samples.Length; i++) { var s = samples[t * Hop + i]; rms += s * s; }
        loudness[t] = MathF.Sqrt(rms / Hop);
        surprise[t] = level0.Observe(audio.Next()).SquaredError;
    }

    var maxS = surprise.DefaultIfEmpty(1).Max();
    var maxL = loudness.DefaultIfEmpty(1).Max();
    const int bucket = 40;   // ~0.4s
    Console.WriteLine("  time   loudness              surprise (prediction error)");
    for (var b = 0; b * bucket < frames; b++)
    {
        float s = 0, l = 0; var n = 0;
        for (var i = b * bucket; i < (b + 1) * bucket && i < frames; i++) { s += surprise[i]; l += loudness[i]; n++; }
        s /= n; l /= n;
        Console.WriteLine($"  {b * bucket / 100f,4:F1}s  {new string('#', (int)(l / maxL * 20)),-20}  {new string('#', (int)(s / maxS * 30)),-30}");
    }

    var third = frames / 3;
    float early = 0, late = 0;
    for (var i = 0; i < third; i++) early += surprise[i];
    for (var i = frames - third; i < frames; i++) late += surprise[i];
    Console.WriteLine($"\n  mean surprise fell from {early / third:F3} (first third) to {late / third:F3} (last third)");
    Console.WriteLine("  — the model learned to predict this sound as it listened.\n");
}

// ---- live microphone mode --------------------------------------------------------------------
static void LiveMicrophone(Cochlea cochlea, Unit level0)
{
    Console.WriteLine("\n  SyntheticMind — listening (make some noise; Ctrl+C to stop)\n");

    var queue = new Queue<float>();
    var gate = new object();
    using var mic = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 20 };
    mic.DataAvailable += (_, e) =>
    {
        lock (gate)
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
                queue.Enqueue(BitConverter.ToInt16(e.Buffer, i) / 32768f);
    };

    float[] Pull()
    {
        while (true)
        {
            lock (gate)
                if (queue.Count >= Hop)
                {
                    var block = new float[Hop];
                    for (var i = 0; i < Hop; i++) block[i] = queue.Dequeue();
                    return block;
                }
            Thread.Sleep(2);
        }
    }
    var audio = new AudioStream(Pull, cochlea, Hop);

    mic.StartRecording();
    var frame = 0;
    var smoothed = 0f;
    const string ramp = " ▁▂▃▄▅▆▇█";
    while (true)
    {
        var mel = audio.Next();
        smoothed += 0.3f * (level0.Observe(mel).SquaredError - smoothed);
        if (++frame % 5 != 0) continue;
        var spectrum = string.Concat(mel.Select(v => ramp[Math.Clamp((int)((v * 0.4f + 0.5f) * 8), 0, 8)]));
        Console.Write($"\r  {spectrum}   surprise {smoothed,6:F3} {new string('#', Math.Clamp((int)(smoothed * 40), 0, 30)),-30}");
    }
}
