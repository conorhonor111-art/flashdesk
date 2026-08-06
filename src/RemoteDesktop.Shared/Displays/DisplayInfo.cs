namespace RemoteDesktop.Shared.Displays;

/// <summary>
/// One screen attached to the machine being helped, as the operator needs to think about it.
///
/// <para><b>Number is a POSITION, not an enumeration index.</b> Windows hands displays back in
/// whatever order the driver reports, which has nothing to do with where they sit on the desk. A
/// person says "the one on the right"; nobody says "the second one as enumerated by the API". So
/// <see cref="Number"/> is assigned left to right by <see cref="X"/> — see
/// <see cref="DisplayLayout.NumberLeftToRight"/> — and it is what the operator's label shows.</para>
///
/// <para><b>Coordinates are the VIRTUAL DESKTOP's, and they can be negative.</b> The primary screen
/// starts at (0,0); a screen placed to its left has a negative <see cref="X"/>, and one placed above
/// it a negative <see cref="Y"/>. That is not an error case to guard against, it is the ordinary
/// arrangement of a machine with two screens, and it is where a click lands on the wrong screen if
/// the arithmetic assumes zero.</para>
/// </summary>
/// <param name="Number">1-based, assigned left to right. What the operator is shown.</param>
/// <param name="Name">Whatever Windows calls it. Never shown to a person on its own — see <see cref="DisplayLayout.Describe"/>.</param>
/// <param name="X">Left edge in virtual-desktop coordinates. May be negative.</param>
/// <param name="Y">Top edge in virtual-desktop coordinates. May be negative.</param>
public sealed record DisplayInfo(
    int Number,
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary)
{
    /// <summary>One past the right edge, in the same way a rectangle's Right normally works.</summary>
    public int Right => X + Width;

    /// <summary>One past the bottom edge.</summary>
    public int Bottom => Y + Height;
}
