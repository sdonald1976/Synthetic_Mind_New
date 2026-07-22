using SyntheticMind.Audio;
using SyntheticMind.Vision;

namespace SyntheticMind.Tests;

/// <summary>
/// Finding 037 — the front-ends for object→word: attention (a salient sub-region, not the whole
/// frame) and word segmentation (word-sized chunks out of continuous audio).
/// </summary>
public class ObjectWordTests
{
    [Fact]
    public void Attention_jumps_to_the_salient_coloured_object()
    {
        const int w = 120, h = 90;
        // Grey background, a bright-red block in the bottom-right quadrant. Attention should land its
        // window on the block — a distinctive coloured thing, not the bland background.
        float[] luma = new float[w * h], red = new float[w * h], green = new float[w * h], blue = new float[w * h];
        Array.Fill(luma, 0.5f); Array.Fill(red, 0.5f); Array.Fill(green, 0.5f); Array.Fill(blue, 0.5f);
        for (var y = h / 2; y < h; y++)
            for (var x = w / 2; x < w; x++)
            { var i = y * w + x; red[i] = 1f; green[i] = 0f; blue[i] = 0f; luma[i] = 0.4f; }

        var attn = new ObjectAttention(new Retina(grid: 6, motion: false, orientations: 4, color: true));
        var (_, x0, y0, ww, hh) = attn.Attend(luma, red, green, blue, w, h);

        // The window's centre should fall inside the red block (the bottom-right quadrant).
        Assert.True(x0 + ww / 2 >= w / 2, $"window centre x {x0 + ww / 2} should be in the right half");
        Assert.True(y0 + hh / 2 >= h / 2, $"window centre y {y0 + hh / 2} should be in the bottom half");
    }

    [Fact]
    public void Novelty_attention_ignores_the_ever_present_thing_and_fires_on_what_appears()
    {
        const int w = 120, h = 90;
        // A "face": a contrasty block always in the CENTRE, plus a NEW object that appears later.
        var attn = new ObjectAttention(new Retina(grid: 6, motion: false, orientations: 4, color: true), mode: AttentionMode.Novelty);
        // Establish the background: many frames of just the centre face.
        for (var t = 0; t < 60; t++)
        {
            float[] l = new float[w * h], r = new float[w * h], gg = new float[w * h], bb = new float[w * h];
            Array.Fill(l, 0.5f); Array.Fill(r, 0.5f); Array.Fill(gg, 0.5f); Array.Fill(bb, 0.5f);
            for (var y = h / 3; y < 2 * h / 3; y++)
                for (var x = w / 3; x < 2 * w / 3; x++)
                { var i = y * w + x; l[i] = 0.9f; r[i] = 0.9f; gg[i] = 0.8f; bb[i] = 0.7f; }
            attn.Attend(l, r, gg, bb, w, h);
        }
        // Now a frame where a new red object appears bottom-right.
        float[] fl = new float[w * h], fr = new float[w * h], fg = new float[w * h], fb = new float[w * h];
        Array.Fill(fl, 0.5f); Array.Fill(fr, 0.5f); Array.Fill(fg, 0.5f); Array.Fill(fb, 0.5f);
        for (var y = h / 3; y < 2 * h / 3; y++)
            for (var x = w / 3; x < 2 * w / 3; x++)
            { var i = y * w + x; fl[i] = 0.9f; fr[i] = 0.9f; fg[i] = 0.8f; fb[i] = 0.7f; }
        for (var y = 3 * h / 4; y < h; y++)
            for (var x = 3 * w / 4; x < w; x++)
            { var i = y * w + x; fr[i] = 1f; fg[i] = 0f; fb[i] = 0f; }

        var (_, x0, y0, ww, hh) = attn.Attend(fl, fr, fg, fb, w, h);
        // Attention should go to the NEW object (bottom-right), not the ever-present centre face.
        Assert.True(x0 + ww / 2 >= w / 2, $"should attend to the new object on the right, got centre-x {x0 + ww / 2}");
        Assert.True(y0 + hh / 2 >= h / 2, $"should attend to the new object at the bottom, got centre-y {y0 + hh / 2}");
    }

    [Fact]
    public void Person_centred_rests_on_the_person_glances_at_a_new_thing_then_returns()
    {
        const int w = 120, h = 90;
        (float[] L, float[] R, float[] G, float[] B) Frame(bool withObject)
        {
            float[] l = new float[w * h], r = new float[w * h], g = new float[w * h], b = new float[w * h];
            Array.Fill(l, 0.5f); Array.Fill(r, 0.5f); Array.Fill(g, 0.5f); Array.Fill(b, 0.5f);
            for (var y = h / 3; y < 2 * h / 3; y++)                       // the ever-present centre "person"
                for (var x = w / 3; x < 2 * w / 3; x++)
                { var i = y * w + x; l[i] = 0.9f; r[i] = 0.9f; g[i] = 0.8f; b[i] = 0.7f; }
            if (withObject)
                for (var y = 3 * h / 4; y < h; y++)                       // a thing held up bottom-right
                    for (var x = 3 * w / 4; x < w; x++)
                    { var i = y * w + x; r[i] = 1f; g[i] = 0f; b[i] = 0f; l[i] = 0.4f; }
            return (l, r, g, b);
        }

        var attn = new ObjectAttention(new Retina(grid: 6, motion: false, orientations: 4, color: true),
            mode: AttentionMode.PersonCentred, glanceFrames: 6, glanceTrigger: 2.5f);

        for (var t = 0; t < 60; t++) { var f = Frame(false); attn.Attend(f.L, f.R, f.G, f.B, w, h); }   // rest on person

        // The person holds something up → attention glances at it (bottom-right).
        (int cx, int cy) g1 = (0, 0);
        for (var t = 0; t < 3; t++) { var f = Frame(true); var a = attn.Attend(f.L, f.R, f.G, f.B, w, h); g1 = (a.X0 + a.W / 2, a.Y0 + a.H / 2); }
        Assert.True(attn.Glancing, "should be glancing at the held-up thing");
        Assert.True(g1.cx >= w / 2 && g1.cy >= h / 2, $"glance should be bottom-right, got {g1}");

        // The thing goes away → after the glance elapses, attention returns to the person (centre).
        (int cx, int cy) last = (0, 0);
        for (var t = 0; t < 12; t++) { var f = Frame(false); var a = attn.Attend(f.L, f.R, f.G, f.B, w, h); last = (a.X0 + a.W / 2, a.Y0 + a.H / 2); }
        Assert.False(attn.Glancing, "the glance should have ended");
        Assert.True(last.cx > w / 4 && last.cx < 3 * w / 4 && last.cy > h / 4 && last.cy < 3 * h / 4,
            $"attention should be back on the centre person, got {last}");
    }

    [Fact]
    public void Word_segmenter_cuts_voiced_runs_and_ignores_blips()
    {
        var seg = new WordSegmenter(melBands: 4, minHops: 10, maxHops: 90, hangHops: 5, activateOverFloor: 4f);
        var quiet = new float[4];
        var loud = new float[] { 1f, 1f, 1f, 1f };

        var words = new List<float[]>();
        void Feed(float[] mel, float energy, int hops)
        {
            for (var i = 0; i < hops; i++) { var w = seg.Accept(mel, energy); if (w is not null) words.Add(w); }
        }

        Feed(quiet, 0.001f, 30);   // settle the noise floor
        Feed(loud, 1f, 25);        // a word (25 hops > min 10)
        Feed(quiet, 0.001f, 20);   // pause → ends the word
        Feed(loud, 1f, 4);         // a blip (4 hops < min 10) — should NOT count
        Feed(quiet, 0.001f, 20);
        Feed(loud, 1f, 25);        // another word
        var last = seg.Flush(); if (last is not null) words.Add(last);

        Assert.Equal(2, words.Count);                       // two words, blip dropped
        Assert.All(words, mw => Assert.True(mw[0] > 0.5f));  // each carries the voiced spectrum
    }

    [Fact]
    public void A_long_run_is_cut_into_word_sized_pieces()
    {
        var seg = new WordSegmenter(melBands: 2, minHops: 10, maxHops: 40, hangHops: 5);
        var loud = new float[] { 1f, 1f };
        var count = 0;
        for (var i = 0; i < 20; i++) seg.Accept(new float[2], 0.001f);                  // quiet preamble sets the floor
        for (var i = 0; i < 200; i++) if (seg.Accept(loud, 1f) is not null) count++;   // 200 hops of continuous voice
        // Continuous speech isn't one giant "word": it's cut at maxHops (40) → ~5 pieces.
        Assert.True(count is >= 4 and <= 6, $"long run should split into word-sized pieces, got {count}");
    }
}
