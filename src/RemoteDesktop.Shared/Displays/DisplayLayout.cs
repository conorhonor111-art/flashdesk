namespace RemoteDesktop.Shared.Displays;

/// <summary>The whole virtual desktop: the smallest rectangle containing every screen.</summary>
/// <param name="Left">May be negative, when a screen sits to the left of the primary.</param>
/// <param name="Top">May be negative, when a screen sits above the primary.</param>
public readonly record struct VirtualBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>
/// The arithmetic that decides where the operator's click lands. Pure functions, no Windows calls,
/// so every case below is a unit test rather than something discovered on someone's desk.
///
/// <para><b>THIS IS THE HIGHEST-RISK PART OF MULTI-MONITOR, and the reason is that being wrong here
/// does not look like a bug.</b> A click that lands 1920 pixels away opens the wrong thing on the
/// wrong screen. The operator sees an unexplained result, the person being helped sees their
/// computer do something nobody asked for, and neither has any way to work out why — least of all
/// that an offset was missing.</para>
///
/// <para><b>ONE COORDINATE SPACE, OR NONE OF THIS IS TRUE.</b> Everything here assumes the display
/// rectangles and the injection space are expressed in the SAME units. On Windows that means
/// physical pixels, and it holds only while the process is per-monitor DPI aware. Under
/// system-DPI awareness Windows reports a differently-scaled screen's rectangle in the PRIMARY
/// screen's units while the capture API reports its real pixels — the two disagree, and this
/// arithmetic would then be correctly computing the wrong answer. That is a process-level setting,
/// not something this file can fix, and it must be settled before mixed-DPI is claimed to work.</para>
/// </summary>
public static class DisplayLayout
{
    /// <summary>
    /// Sorts left to right by X and renumbers from 1, discarding whatever order Windows gave.
    /// Screens at the same X are then ordered top to bottom, so a stacked pair is still stable.
    /// </summary>
    public static IReadOnlyList<DisplayInfo> NumberLeftToRight(IEnumerable<DisplayInfo> displays)
    {
        var ordered = displays.OrderBy(d => d.X).ThenBy(d => d.Y).ToList();
        var result = new List<DisplayInfo>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
            result.Add(ordered[i] with { Number = i + 1 });
        return result;
    }

    /// <summary>The smallest rectangle containing every screen. Empty input gives an empty box.</summary>
    public static VirtualBounds BoundsOf(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) return new VirtualBounds(0, 0, 0, 0);

        int left = displays.Min(d => d.X);
        int top = displays.Min(d => d.Y);
        int right = displays.Max(d => d.Right);
        int bottom = displays.Max(d => d.Bottom);
        return new VirtualBounds(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Turns a pixel in the picture the operator is looking at into a point on the whole virtual
    /// desktop. The picture always starts at its screen's top-left, so this is the screen's own
    /// offset added — and that offset is exactly what is negative for a screen on the left.
    /// </summary>
    public static (int X, int Y) ToVirtual(DisplayInfo display, int imageX, int imageY)
        => (display.X + imageX, display.Y + imageY);

    /// <summary>
    /// Turns a virtual-desktop point into the 0..65535 pair SendInput wants with
    /// MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK.
    ///
    /// <para>⚠ VIRTUALDESK IS NOT OPTIONAL AND IS NOT SET TODAY. Without it Windows spreads
    /// 0..65535 across the PRIMARY SCREEN ONLY, so every coordinate computed here would be squeezed
    /// onto screen one — which is precisely why the current single-screen code works and why it
    /// would silently keep "working" while sending every click to the wrong place.</para>
    ///
    /// <para>The subtraction of Left and Top is what handles negative origins: a desktop running
    /// from -1920 to 1920 maps -1920 to 0, not to something negative.</para>
    /// </summary>
    public static (int Nx, int Ny) ToAbsolute(VirtualBounds bounds, int virtualX, int virtualY)
    {
        // Dividing by (size - 1) rather than size, so the last pixel reaches 65535 exactly. Matches
        // what the single-screen path already does; changing it would move every existing click by
        // a fraction of a pixel for no reason.
        int nx = (int)((virtualX - bounds.Left) * 65535L / Math.Max(1, bounds.Width - 1));
        int ny = (int)((virtualY - bounds.Top) * 65535L / Math.Max(1, bounds.Height - 1));
        return (nx, ny);
    }

    /// <summary>
    /// The words the operator sees: "Screen 1 of 2 — left". Never the device name — a stranger does
    /// not know what "DELL U2415" is, and neither does the operator when someone reads it aloud.
    /// </summary>
    public static string Describe(int number, int count)
        => count <= 1 ? "Screen 1" : $"Screen {number} of {count} — {PositionWord(number, count)}";

    /// <summary>
    /// Where a screen sits, in the word a person would use. With two screens it is left or right;
    /// with three the middle one is "middle"; beyond that the position word stops meaning anything
    /// useful and the number carries it alone.
    /// </summary>
    public static string PositionWord(int number, int count)
    {
        if (count <= 1) return "the only one";
        if (number == 1) return "left";
        if (number == count) return "right";
        if (count == 3) return "middle";
        return $"{number} from the left";
    }
}
