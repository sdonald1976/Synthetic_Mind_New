using SyntheticMind.Mind;
using SyntheticMind.Vision;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 020 — grounding, the whole loop. A script teaches five distinct moving "things" by name,
/// binding each to its perceptual (retina) summary in a <see cref="ConceptStore"/>; then it quizzes
/// on fresh, varied instances. This is Slice 0's "teach it a thing, ask what it is", on the real
/// pipeline. Self-contained — the shapes are rendered here.
/// </summary>
public class GroundingTests
{
    private const int Size = 32, Grid = 8, Clip = 24;
    private static readonly string[] Names = ["rightward", "downward", "diagonal", "orbit", "blink"];

    [Fact]
    public void Teaches_five_things_by_name_and_recognizes_fresh_instances()
    {
        var rng = new Random(1);
        var store = new ConceptStore();

        // Teach: 8 examples of each thing, with the label supplied by the "script".
        for (var c = 0; c < Names.Length; c++)
            for (var i = 0; i < 8; i++)
                store.Teach(Names[c], Summary(Render(c, rng)));

        // Quiz: 10 fresh instances of each; recognition should be right far above chance (20%).
        var correct = 0; var total = 0;
        for (var c = 0; c < Names.Length; c++)
            for (var i = 0; i < 10; i++)
            {
                var recognized = store.Recall(Summary(Render(c, rng)));
                Assert.NotNull(recognized);
                if (recognized!.Value.Name == Names[c]) correct++;
                total++;
            }

        Assert.True((float)correct / total > 0.8f, $"grounding accuracy {100f * correct / total:F0}% (chance 20%)");
    }

    // clip → normalized average retina summary (perception, where categorical structure lives)
    private static float[] Summary(List<float[]> frames)
    {
        var retina = new Retina(Grid, motion: true);
        var acc = new float[retina.Width];
        var n = 0;
        for (var t = 0; t < frames.Count; t++)
        {
            var feat = retina.Process(frames[t], Size, Size);
            if (t == 0) continue;   // no motion baseline on the first frame
            for (var i = 0; i < feat.Length; i++) acc[i] += feat[i];
            n++;
        }
        for (var i = 0; i < acc.Length; i++) acc[i] /= n;
        return acc;
    }

    // render one clip of a "thing", with per-instance variation (speed, phase, jitter, noise)
    private static List<float[]> Render(int thing, Random r)
    {
        var speed = 0.8f + (float)r.NextDouble() * 0.4f;
        var ph0 = (float)r.NextDouble();
        float ox = (float)(r.NextDouble() * 4 - 2), oy = (float)(r.NextDouble() * 4 - 2);
        var frames = new List<float[]>();
        for (var t = 0; t < Clip; t++)
        {
            var f = new float[Size * Size];
            var u = t * speed / Clip + ph0;
            float px = 16 + ox, py = 16 + oy, rad = 4;
            if (thing == 0) { px = 4 + u % 1f * 24 + ox; py = 16 + oy; }
            else if (thing == 1) { px = 16 + ox; py = 4 + u % 1f * 24 + oy; }
            else if (thing == 2) { px = 4 + u % 1f * 24 + ox; py = 4 + u % 1f * 24 + oy; }
            else if (thing == 3) { px = 16 + 10 * MathF.Cos(2 * MathF.PI * u) + ox; py = 16 + 10 * MathF.Sin(2 * MathF.PI * u) + oy; }

            if (thing == 4)
            {
                var b = (MathF.Sin(2 * MathF.PI * u * 2) + 1) / 2 * 0.8f;
                for (var i = 0; i < f.Length; i++) f[i] = b;
            }
            else
            {
                for (var y = 0; y < Size; y++)
                    for (var x = 0; x < Size; x++)
                    {
                        var d = MathF.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                        if (d < rad) f[y * Size + x] = 1f;
                    }
            }
            for (var i = 0; i < f.Length; i++) f[i] = Math.Clamp(f[i] + (float)(r.NextDouble() * 0.06 - 0.03), 0, 1);
            frames.Add(f);
        }
        return frames;
    }
}
