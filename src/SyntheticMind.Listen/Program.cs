using NAudio.Wave;
using SyntheticMind.Audio;
using SyntheticMind.Mind;

// Live microphone → cochlea → hierarchy. Speak, and watch the model hear.
//
// The two things on screen:
//   SPECTRUM  — the cochlea's mel bands right now (what frequencies are present)
//   SURPRISE  — how wrong level 0's prediction of this instant was. It spikes on changes
//               (a new sound, a word onset) and settles during steady sound or silence.
//               That spike is the learning signal — the model being wrong, which is how it learns.

const int SampleRate = 16000;
const int Hop = 160;          // 100 mel-frames per second
const int MelBands = 20;

Console.WriteLine();
Console.WriteLine("  SyntheticMind — listening");
Console.WriteLine("  make some noise (talk, hum, tap). Ctrl+C to stop.");
Console.WriteLine();

// --- microphone capture: NAudio pushes buffers; we queue the samples for the pull-based stream ---
var sampleQueue = new Queue<float>();
var queueLock = new object();

using var mic = new WaveInEvent
{
    WaveFormat = new WaveFormat(SampleRate, 16, 1),   // 16 kHz, 16-bit, mono
    BufferMilliseconds = 20,
};

mic.DataAvailable += (_, e) =>
{
    lock (queueLock)
        for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            sampleQueue.Enqueue(BitConverter.ToInt16(e.Buffer, i) / 32768f);
};

float[] PullSamples()
{
    // Hand the stream one hop of samples, waiting briefly if the mic hasn't delivered enough yet.
    while (true)
    {
        lock (queueLock)
        {
            if (sampleQueue.Count >= Hop)
            {
                var block = new float[Hop];
                for (var i = 0; i < Hop; i++) block[i] = sampleQueue.Dequeue();
                return block;
            }
        }
        Thread.Sleep(2);
    }
}

// --- the pipeline: cochlea → learned level 0 → a slow level ---
var cochlea = new Cochlea(SampleRate, fftSize: 512, melBands: MelBands);
var audio = new AudioStream(PullSamples, cochlea, Hop);
// No quad features (pitch/timbre are linear in the mel spectrum). Default rate — NLMS normalization
// (finding 013) makes the encoder scale-free, so no hand-tuning for audio.
var level0 = new Unit(new LearnedPredictiveRule(MelBands, stateWidth: 8, history: 8, quadraticFeatures: 0));
var slow = new TemporalLevel(inputWidth: 8, stride: 25, integratorRate: 0.05f);  // ~quarter-second summary

mic.StartRecording();

var frame = 0;
var smoothedSurprise = 0f;
const string ramp = " ▁▂▃▄▅▆▇█";

while (true)
{
    var mel = audio.Next();
    var tick = level0.Observe(mel);
    slow.Observe(tick.State);
    frame++;

    // Prediction error = surprise. Smooth it a touch so the display isn't jittery.
    smoothedSurprise += 0.3f * (tick.SquaredError - smoothedSurprise);

    if (frame % 5 != 0) continue;   // redraw ~20x/second

    var spectrum = string.Concat(mel.Select(v =>
        ramp[Math.Clamp((int)(Normalize(v) * (ramp.Length - 1)), 0, ramp.Length - 1)]));

    var surpriseBar = new string('#', Math.Clamp((int)(smoothedSurprise * 40f), 0, 30));
    Console.Write($"\r  {spectrum}   surprise {smoothedSurprise,6:F3} {surpriseBar,-30}");
}

// Map a normalized mel value (roughly zero-mean, unit-ish) into [0,1] for the display ramp.
static float Normalize(float v) => Math.Clamp(v * 0.4f + 0.5f, 0f, 1f);
