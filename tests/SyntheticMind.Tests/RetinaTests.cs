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
}
