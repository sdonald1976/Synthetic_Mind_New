using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using NAudio.Wave;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

// Live cross-modal grounding: listen and watch at once, and BIND what's heard to what's seen.
//   dotnet run --project src/SyntheticMind.Perceive
//
//   EAR: microphone -> cochlea -> level 0        EYE: webcam -> retina -> level 0
//
// Keys:
//   SPACE  bind — make a distinct SOUND while doing a distinct VISUAL thing; binds the pair
//   A      hear->see — make just the sound; it recalls which bound pair it matches
//   V      see->hear — do just the visual; it recalls which pair it matches
//   Q      quit
//
// Coarse 8x8 retina: works on GROSS visual differences (wave left vs right, lean in, cover lens),
// not fine objects. Pair each with a distinct sound ("ahh" vs "ooo", clap, whistle).

const int SampleRate = 16000, Hop = 160, MelBands = 20, Grid = 8, CamW = 80, CamH = 60;
var VisualWidth = Grid * Grid * 2;

Console.WriteLine();
Console.WriteLine("  SyntheticMind — live cross-modal grounding");
Console.WriteLine("  SPACE = bind sound+sight    A = hear->see    V = see->hear    Q = quit\n");

var running = true;
var gate = new object();
float earSurprise = 0, eyeSurprise = 0;
var eyeOk = false;
var earSummary = new float[MelBands];      // rolling average of recent mel frames (perception)
var eyeSummary = new float[VisualWidth];   // rolling average of recent retina frames
var bindings = new List<(float[] Audio, float[] Visual)>();

// ---- EAR ------------------------------------------------------------------------------------
var samples = new Queue<float>();
var micGate = new object();
WaveInEvent? mic = null;
try
{
    mic = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 20 };
    mic.DataAvailable += (_, e) =>
    {
        lock (micGate)
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
                samples.Enqueue(BitConverter.ToInt16(e.Buffer, i) / 32768f);
    };
    mic.StartRecording();
}
catch (Exception ex) { Console.WriteLine($"  (no microphone: {ex.Message})"); }

var earThread = new Thread(() =>
{
    var cochlea = new Cochlea(SampleRate, 512, MelBands);
    var level0 = new Unit(new LearnedPredictiveRule(MelBands, stateWidth: 8, history: 8, quadraticFeatures: 0));
    float[] Pull()
    {
        while (running)
        {
            lock (micGate)
                if (samples.Count >= Hop)
                {
                    var b = new float[Hop];
                    for (var i = 0; i < Hop; i++) b[i] = samples.Dequeue();
                    return b;
                }
            Thread.Sleep(2);
        }
        return new float[Hop];
    }
    var audio = new AudioStream(Pull, cochlea, Hop, normalize: false);
    while (running)
    {
        var mel = audio.Next();
        var s = level0.Observe(mel).SquaredError;
        lock (gate)
        {
            earSurprise += 0.3f * (s - earSurprise);
            for (var i = 0; i < MelBands; i++) earSummary[i] += 0.04f * (mel[i] - earSummary[i]);  // ~0.5s window
        }
    }
}) { IsBackground = true };
if (mic is not null) earThread.Start();

// ---- EYE ------------------------------------------------------------------------------------
var eyeThread = new Thread(() =>
{
    VideoCapture? cam = null;
    try { cam = new VideoCapture(0); } catch { }
    if (cam is null || !cam.IsOpened()) { Console.WriteLine("  (no webcam on device 0 - ear only)"); return; }
    lock (gate) eyeOk = true;

    var retina = new Retina(Grid, motion: true);
    var level0 = new Unit(new LearnedPredictiveRule(VisualWidth, stateWidth: 12, history: 6, quadraticFeatures: 0));
    using var frame = new Mat();
    using var grayMat = new Mat();
    using var small = new Mat();
    var pixels = new float[CamW * CamH];
    var bytes = new byte[CamW * CamH];

    while (running)
    {
        if (!cam.Read(frame) || frame.Empty()) { Thread.Sleep(5); continue; }
        Cv2.CvtColor(frame, grayMat, ColorConversionCodes.BGR2GRAY);
        Cv2.Resize(grayMat, small, new Size(CamW, CamH));
        Marshal.Copy(small.Data, bytes, 0, bytes.Length);
        for (var i = 0; i < bytes.Length; i++) pixels[i] = bytes[i] / 255f;

        var feat = retina.Process(pixels, CamW, CamH);
        var s = level0.Observe(feat).SquaredError;
        lock (gate)
        {
            eyeSurprise += 0.3f * (s - eyeSurprise);
            for (var i = 0; i < VisualWidth; i++) eyeSummary[i] += 0.1f * (feat[i] - eyeSummary[i]);  // ~0.5s window
        }
    }
    cam.Dispose();
}) { IsBackground = true };
eyeThread.Start();

// ---- interaction + display ------------------------------------------------------------------
float[] Unit(float[] v) { var n = TensorPrimitives.Norm<float>(v) + 1e-6f; var r = new float[v.Length]; TensorPrimitives.Divide<float>(v, n, r); return r; }
int Nearest(float[] q, Func<(float[] Audio, float[] Visual), float[]> key)
{
    var u = Unit(q); var best = -1; var bd = float.MaxValue;
    for (var i = 0; i < bindings.Count; i++)
    {
        var k = Unit(key(bindings[i])); var d = 0f;
        for (var j = 0; j < u.Length; j++) { var e = u[j] - k[j]; d += e * e; }
        if (d < bd) { bd = d; best = i; }
    }
    return best;
}
static string Bar(float v, float scale) => new('#', Math.Clamp((int)(v * scale), 0, 20));
var consoleGate = new object();

// Dedicated blocking key-reader thread — more reliable than polling KeyAvailable, and it prints
// feedback for EVERY key so we can see whether input reaches the program at all.
var inputThread = new Thread(() =>
{
    while (running)
    {
        ConsoleKeyInfo info;
        try { info = Console.ReadKey(true); }
        catch { Thread.Sleep(100); continue; }   // input not available in this terminal

        float[] a, v;
        lock (gate) { a = (float[])earSummary.Clone(); v = (float[])eyeSummary.Clone(); }

        string msg = info.Key switch
        {
            ConsoleKey.Q or ConsoleKey.Escape => "quitting",
            ConsoleKey.Spacebar => $"BOUND pair #{bindings.Count} (total {bindings.Count + 1})",
            ConsoleKey.A when bindings.Count > 0 => $"HEARD -> matches pair #{Nearest(a, b => b.Audio)}",
            ConsoleKey.V when bindings.Count > 0 => $"SAW   -> matches pair #{Nearest(v, b => b.Visual)}",
            ConsoleKey.A or ConsoleKey.V => "nothing bound yet — press SPACE first",
            _ => $"got key '{info.Key}'  (SPACE = bind, A/V = recall, Q = quit)",
        };
        if (info.Key == ConsoleKey.Spacebar) bindings.Add((a, v));
        if (info.Key is ConsoleKey.Q or ConsoleKey.Escape) running = false;

        lock (consoleGate) Console.WriteLine($"\n  >> {msg}");
    }
}) { IsBackground = true };
inputThread.Start();

while (running)
{
    float ear, eye; bool ready;
    lock (gate) { ear = earSurprise; eye = eyeSurprise; ready = eyeOk; }
    var eyeCol = ready ? $"{eye,7:F3} {Bar(eye, 300f),-20}" : "(no camera)        ";
    lock (consoleGate) Console.Write($"\r  EAR {ear,7:F3} {Bar(ear, 40f),-20}   EYE {eyeCol}   bound:{bindings.Count} ");
    Thread.Sleep(80);
}

mic?.StopRecording();
mic?.Dispose();
Console.WriteLine("\n\n  stopped.\n");
