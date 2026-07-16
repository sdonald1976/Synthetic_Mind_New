using System.Runtime.InteropServices;
using NAudio.Wave;
using OpenCvSharp;
using SyntheticMind.Audio;
using SyntheticMind.Mind;
using SyntheticMind.Vision;

// Live: listen and watch at the SAME time, on real hardware.
//   dotnet run --project src/SyntheticMind.Perceive
//
// Two senses run at their own rates through the same architecture:
//   EAR   — microphone → cochlea → level 0
//   EYE   — webcam     → retina  → level 0
// Each shows SURPRISE (prediction error): talk or make a noise → the ear spikes; move in front of
// the camera → the eye spikes. Both settle when things are steady. Press a key to stop.

const int SampleRate = 16000, Hop = 160, MelBands = 20;
const int CamW = 80, CamH = 60, Grid = 8;

Console.WriteLine();
Console.WriteLine("  SyntheticMind — perceiving (listening + watching)");
Console.WriteLine("  talk / make noise  -> the EAR should spike");
Console.WriteLine("  move in view       -> the EYE should spike");
Console.WriteLine("  press any key to stop\n");

var running = true;
var gate = new object();
var earSurprise = 0f;
var eyeSurprise = 0f;
var eyeOk = false;

// ---- EAR: microphone -> cochlea -> level 0 --------------------------------------------------
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
        var s = level0.Observe(audio.Next()).SquaredError;
        lock (gate) earSurprise += 0.3f * (s - earSurprise);
    }
}) { IsBackground = true };
if (mic is not null) earThread.Start();

// ---- EYE: webcam -> retina -> level 0 -------------------------------------------------------
var eyeThread = new Thread(() =>
{
    VideoCapture? cam = null;
    try { cam = new VideoCapture(0); } catch { /* handled below */ }
    if (cam is null || !cam.IsOpened())
    {
        Console.WriteLine("  (no webcam found on device 0 - running ear-only)");
        return;
    }
    lock (gate) eyeOk = true;

    var retina = new Retina(Grid, motion: true);
    var level0 = new Unit(new LearnedPredictiveRule(retina.Width, stateWidth: 12, history: 6, quadraticFeatures: 0));
    using var frame = new Mat();
    using var gray = new Mat();
    using var small = new Mat();
    var pixels = new float[CamW * CamH];
    var bytes = new byte[CamW * CamH];

    while (running)
    {
        if (!cam.Read(frame) || frame.Empty()) { Thread.Sleep(5); continue; }
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Resize(gray, small, new Size(CamW, CamH));
        Marshal.Copy(small.Data, bytes, 0, bytes.Length);
        for (var i = 0; i < bytes.Length; i++) pixels[i] = bytes[i] / 255f;

        var s = level0.Observe(retina.Process(pixels, CamW, CamH)).SquaredError;
        lock (gate) eyeSurprise += 0.3f * (s - eyeSurprise);
    }
    cam.Dispose();
}) { IsBackground = true };
eyeThread.Start();

// ---- display: both senses, live ------------------------------------------------------------
static string Bar(float v, float scale, int max) => new('#', Math.Clamp((int)(v * scale), 0, max));
while (!Console.KeyAvailable)
{
    float ear, eye; bool eyeReady;
    lock (gate) { ear = earSurprise; eye = eyeSurprise; eyeReady = eyeOk; }
    var eyeCol = eyeReady ? $"{eye,7:F3} {Bar(eye, 300f, 24)}" : "(no camera)";
    Console.Write($"\r  EAR {ear,7:F3} {Bar(ear, 40f, 24),-24}   EYE {eyeCol,-34}");
    Thread.Sleep(50);
}

running = false;
Console.ReadKey(true);
mic?.StopRecording();
mic?.Dispose();
Console.WriteLine("\n\n  stopped.\n");
