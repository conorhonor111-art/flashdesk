using RemoteDesktop.Shared.Displays;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The coordinate translation for multi-monitor, written before the capture path is touched.
///
/// This is the part of multi-monitor that fails invisibly. A missing offset does not crash and does
/// not log; it opens the wrong thing on the wrong screen, and neither the operator nor the person
/// being helped has any way to work out why. Every arrangement below is one somebody actually has
/// on their desk.
/// </summary>
public class DisplayLayoutTests
{
    private static DisplayInfo Screen(int x, int y, int w = 1920, int h = 1080, bool primary = false)
        => new(0, "test", x, y, w, h, primary);

    // ------------------------------------------------------------------ one screen: nothing moves

    [Fact]
    public void One_screen_maps_a_pixel_to_itself()
    {
        var only = Screen(0, 0, primary: true);
        Assert.Equal((0, 0), DisplayLayout.ToVirtual(only, 0, 0));
        Assert.Equal((1919, 1079), DisplayLayout.ToVirtual(only, 1919, 1079));
    }

    [Fact]
    public void One_screen_spans_the_whole_absolute_range()
    {
        var bounds = DisplayLayout.BoundsOf(new[] { Screen(0, 0, primary: true) });
        Assert.Equal((0, 0), DisplayLayout.ToAbsolute(bounds, 0, 0));
        Assert.Equal((65535, 65535), DisplayLayout.ToAbsolute(bounds, 1919, 1079));
    }

    // ------------------------------------------------------------------ a second screen, right

    [Fact]
    public void A_screen_to_the_right_offsets_by_its_own_left_edge()
    {
        var right = Screen(1920, 0);
        Assert.Equal((1920, 0), DisplayLayout.ToVirtual(right, 0, 0));
        Assert.Equal((3839, 1079), DisplayLayout.ToVirtual(right, 1919, 1079));
    }

    // ------------------------------------------------------------------ NEGATIVE X: screen on the left

    [Fact]
    public void A_screen_to_the_LEFT_has_negative_coordinates_and_still_maps()
    {
        // The ordinary arrangement when someone puts the second monitor on the left. If the
        // arithmetic assumes zero, every click here lands on the primary instead.
        var left = Screen(-1920, 0);
        Assert.Equal((-1920, 0), DisplayLayout.ToVirtual(left, 0, 0));
        Assert.Equal((-1, 1079), DisplayLayout.ToVirtual(left, 1919, 1079));
    }

    [Fact]
    public void A_negative_origin_maps_to_zero_in_absolute_space_not_to_a_negative_number()
    {
        // The whole desktop runs from -1920 to 1919. SendInput takes 0..65535 only, so the leftmost
        // pixel must come out as 0. A negative here would be silently clamped by Windows and every
        // click on the left screen would pile onto its left edge.
        var bounds = DisplayLayout.BoundsOf(new[] { Screen(-1920, 0), Screen(0, 0, primary: true) });
        Assert.Equal(-1920, bounds.Left);
        Assert.Equal(3840, bounds.Width);

        Assert.Equal(0, DisplayLayout.ToAbsolute(bounds, -1920, 0).Nx);
        Assert.Equal(65535, DisplayLayout.ToAbsolute(bounds, 1919, 0).Nx);

        // The join between the two screens lands in the middle, give or take a rounding step.
        int middle = DisplayLayout.ToAbsolute(bounds, 0, 0).Nx;
        Assert.InRange(middle, 32700, 32800);
    }

    // ------------------------------------------------------------------ NEGATIVE Y: screen above

    [Fact]
    public void A_screen_ABOVE_the_primary_has_a_negative_top_and_still_maps()
    {
        var above = Screen(0, -1080);
        Assert.Equal((0, -1080), DisplayLayout.ToVirtual(above, 0, 0));
        Assert.Equal((0, -1), DisplayLayout.ToVirtual(above, 0, 1079));

        var bounds = DisplayLayout.BoundsOf(new[] { above, Screen(0, 0, primary: true) });
        Assert.Equal(-1080, bounds.Top);
        Assert.Equal(2160, bounds.Height);
        Assert.Equal(0, DisplayLayout.ToAbsolute(bounds, 0, -1080).Ny);
        Assert.Equal(65535, DisplayLayout.ToAbsolute(bounds, 0, 1079).Ny);
    }

    // ------------------------------------------------------------------ mixed resolutions

    [Fact]
    public void Two_screens_of_different_resolutions_keep_their_own_geometry()
    {
        // A 1920x1080 primary with an older 1280x1024 beside it. The desktop is as tall as the
        // TALLER screen, so the shorter one does not fill it and a click near its bottom must not
        // be stretched.
        var primary = Screen(0, 0, 1920, 1080, primary: true);
        var older = Screen(1920, 0, 1280, 1024);
        var bounds = DisplayLayout.BoundsOf(new[] { primary, older });

        Assert.Equal(3200, bounds.Width);
        Assert.Equal(1080, bounds.Height);

        // Bottom-right pixel of the SHORTER screen is at virtual (3199, 1023) - not (3199, 1079).
        Assert.Equal((3199, 1023), DisplayLayout.ToVirtual(older, 1279, 1023));

        // And in absolute space that is short of the bottom, because the desktop is taller than it.
        var (_, ny) = DisplayLayout.ToAbsolute(bounds, 3199, 1023);
        Assert.True(ny < 65535, "the shorter screen's last row must not map to the desktop's bottom");
    }

    [Fact]
    public void A_taller_secondary_makes_the_desktop_taller_than_the_primary()
    {
        var primary = Screen(0, 0, 1920, 1080, primary: true);
        var portrait = Screen(1920, -420, 1080, 1920); // a rotated monitor, top above the primary
        var bounds = DisplayLayout.BoundsOf(new[] { primary, portrait });

        Assert.Equal(-420, bounds.Top);
        Assert.Equal(1920, bounds.Height);
        Assert.Equal(3000, bounds.Width);
    }

    // ------------------------------------------------------------------ mixed DPI

    /// <summary>
    /// A screen running at 150% is not a special case for THIS arithmetic — it simply reports more
    /// physical pixels. A 3840x2160 panel at 150% presents 3840x2160 to a per-monitor-aware
    /// process, and the maths below is the same as for any other size.
    ///
    /// ⚠ WHAT IT DOES DEPEND ON is that the numbers reaching it are physical pixels from ONE source.
    /// Under system-DPI awareness Windows reports such a screen's rectangle scaled into the primary
    /// screen's units, while the capture API keeps reporting its real ones — and then this
    /// arithmetic is correct about the wrong numbers. That is a process-level setting, and this test
    /// exists to record that the dependency is known rather than to prove it is satisfied.
    /// </summary>
    [Fact]
    public void A_higher_dpi_screen_is_just_a_bigger_rectangle_to_this_arithmetic()
    {
        var primary = Screen(0, 0, 1920, 1080, primary: true);   // 100%
        var hidpi = Screen(1920, 0, 3840, 2160);                 // 4K panel at 150%, per-monitor aware
        var bounds = DisplayLayout.BoundsOf(new[] { primary, hidpi });

        Assert.Equal(5760, bounds.Width);
        Assert.Equal(2160, bounds.Height);
        Assert.Equal((5759, 2159), DisplayLayout.ToVirtual(hidpi, 3839, 2159));
        Assert.Equal((65535, 65535), DisplayLayout.ToAbsolute(bounds, 5759, 2159));
    }

    // ------------------------------------------------------------------ numbering and words

    [Fact]
    public void Screens_are_numbered_left_to_right_whatever_order_Windows_gave_them()
    {
        // Windows enumerated the RIGHT-hand screen first. A person still calls it "the right one".
        var given = new[] { Screen(1920, 0), Screen(-1920, 0), Screen(0, 0, primary: true) };
        var ordered = DisplayLayout.NumberLeftToRight(given);

        Assert.Equal(new[] { -1920, 0, 1920 }, ordered.Select(d => d.X));
        Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(d => d.Number));
    }

    [Fact]
    public void Stacked_screens_at_the_same_x_are_ordered_top_to_bottom()
    {
        var ordered = DisplayLayout.NumberLeftToRight(new[] { Screen(0, 1080), Screen(0, 0, primary: true) });
        Assert.Equal(new[] { 0, 1080 }, ordered.Select(d => d.Y));
    }

    [Theory]
    [InlineData(1, 1, "Screen 1")]
    [InlineData(1, 2, "Screen 1 of 2 — left")]
    [InlineData(2, 2, "Screen 2 of 2 — right")]
    [InlineData(2, 3, "Screen 2 of 3 — middle")]
    [InlineData(3, 4, "Screen 3 of 4 — 3 from the left")]
    public void Screens_are_described_by_position_never_by_device_name(int number, int count, string expected)
        => Assert.Equal(expected, DisplayLayout.Describe(number, count));

    // ------------------------------------------------------------------ degenerate input

    [Fact]
    public void No_screens_gives_an_empty_box_rather_than_throwing()
    {
        // Reached in real life for a moment during a display change - a monitor unplugged, the set
        // re-enumerated before the new one is registered. It must not take the session down.
        var bounds = DisplayLayout.BoundsOf(Array.Empty<DisplayInfo>());
        Assert.Equal(new VirtualBounds(0, 0, 0, 0), bounds);
        Assert.Equal((0, 0), DisplayLayout.ToAbsolute(bounds, 0, 0));
    }

    [Fact]
    public void A_one_pixel_screen_does_not_divide_by_zero()
    {
        var bounds = DisplayLayout.BoundsOf(new[] { Screen(0, 0, 1, 1, primary: true) });
        Assert.Equal((0, 0), DisplayLayout.ToAbsolute(bounds, 0, 0));
    }
}
