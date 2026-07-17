using SyntheticMind.Vision;

namespace SyntheticMind.Tests;

public class RetinaTests
{
    [Fact]
    public void Brightness_grid_locates_a_bright_region()
    {
        // 16x16 frame, bright square in the top-left quadrant; the rest dark.
        var px = new float[16 * 16];
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) px[y * 16 + x] = 1f;

        var retina = new Retina(grid: 4, motion: false);
        var f = retina.Process(px, 16, 16);

        // 4x4 grid: cells (0,0),(0,1),(1,0),(1,1) cover the bright quadrant → ~1; others ~0.
        Assert.True(f[0] > 0.9f && f[1] > 0.9f && f[4] > 0.9f && f[5] > 0.9f, "top-left cells should be bright");
        Assert.True(f[10] < 0.1f && f[15] < 0.1f, "bottom-right cells should be dark");
    }

    [Fact]
    public void Motion_channel_lights_where_the_frame_changed()
    {
        var retina = new Retina(grid: 4, motion: true);
        var blank = new float[16 * 16];

        retina.Process(blank, 16, 16);   // first frame: no motion baseline yet

        var moved = new float[16 * 16];
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++) moved[y * 16 + x] = 1f;  // top-left cell turns on
        var f = retina.Process(moved, 16, 16);

        var motion = f.AsSpan(16);   // second half is the motion grid (4x4)
        Assert.True(motion[0] > 0.5f, "the changed cell should show motion");
        Assert.True(motion[15] < 0.1f, "unchanged cells should show no motion");
    }

    [Fact]
    public void A_still_frame_produces_no_motion()
    {
        var retina = new Retina(grid: 4, motion: true);
        var frame = new float[16 * 16];
        for (var i = 0; i < frame.Length; i++) frame[i] = 0.5f;

        retina.Process(frame, 16, 16);
        var f = retina.Process(frame, 16, 16);   // identical frame
        Assert.All(f.AsSpan(16).ToArray(), m => Assert.True(m < 1e-6f, "no change → no motion"));
    }

    [Fact]
    public void Oriented_edges_distinguish_a_vertical_from_a_horizontal_edge()
    {
        const int w = 32, h = 32, orient = 4;
        var retina = new Retina(grid: 4, motion: false, orientations: orient);

        // Vertical edge: left half dark, right half bright -> gradient is horizontal -> first bin.
        var vertical = new float[w * h];
        for (var y = 0; y < h; y++) for (var x = w / 2; x < w; x++) vertical[y * w + x] = 1f;
        var vf = retina.Process(vertical, w, h);

        // Horizontal edge: top half dark, bottom half bright -> gradient vertical -> middle bin.
        var retina2 = new Retina(grid: 4, motion: false, orientations: orient);
        var horizontal = new float[w * h];
        for (var y = h / 2; y < h; y++) for (var x = 0; x < w; x++) horizontal[y * w + x] = 1f;
        var hf = retina2.Process(horizontal, w, h);

        // Edge bins start after the 16 brightness cells (no motion). Sum each orientation across cells.
        float[] EnergyPerOrientation(float[] f)
        {
            var e = new float[orient];
            for (var cell = 0; cell < 16; cell++)
                for (var o = 0; o < orient; o++) e[o] += f[16 + cell * orient + o];
            return e;
        }
        var ve = EnergyPerOrientation(vf);
        var he = EnergyPerOrientation(hf);

        // Vertical edge -> bin 0 (horizontal gradient) dominates; horizontal edge -> bin 2.
        Assert.True(ve[0] > ve[2], $"vertical edge should light bin 0, got {ve[0]:F2} vs {ve[2]:F2}");
        Assert.True(he[2] > he[0], $"horizontal edge should light bin 2, got {he[2]:F2} vs {he[0]:F2}");
    }

    [Fact]
    public void Edges_widen_the_feature_vector()
    {
        Assert.Equal(4 * 4 * (1 + 1 + 4), new Retina(grid: 4, motion: true, orientations: 4).Width);
        Assert.Equal(4 * 4, new Retina(grid: 4, motion: false, orientations: 0).Width);
    }
}
