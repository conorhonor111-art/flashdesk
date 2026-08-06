using System.Windows.Forms;
using RemoteDesktop.Shared.Displays;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RemoteDesktop.Host.Capture;

/// <summary>
/// Finds the screens attached to this machine, in a form both capture and input can agree on.
///
/// <para><b>ONE SOURCE, OR THE INDEXES DISAGREE.</b> DXGI hands out outputs in its own order and
/// Windows hands out monitors in another. If capture picked "output 2" and input offset by
/// "monitor 2" from a different list, clicks would land on the wrong screen while everything looked
/// correct. So the DXGI outputs ARE the list whenever DXGI is available: their
/// <c>DesktopCoordinates</c> give the same virtual-desktop rectangle the injection maths needs, and
/// the position in that list is the same index the duplication is created from.</para>
///
/// <para>The GDI fallback path has no outputs, so it uses <see cref="Screen.AllScreens"/>, which is
/// the same list GDI captures from.</para>
///
/// <para><b>⚠ UNVERIFIED ON REAL HARDWARE.</b> Everything here has only ever run on a machine with
/// ONE screen. The single-display path is exercised constantly; the multi-display branches have
/// never seen a second monitor. See <see cref="Describe"/>, which exists so the first real
/// two-screen machine settles it in one line instead of a debugging session.</para>
/// </summary>
internal static class DisplayEnumerator
{
    /// <summary>
    /// Every screen, numbered left to right by position rather than by the order the API gave.
    /// Returns an empty list only if nothing could be enumerated at all.
    /// </summary>
    internal static IReadOnlyList<DisplayInfo> All()
    {
        var fromDxgi = TryDxgi();
        if (fromDxgi.Count > 0) return DisplayLayout.NumberLeftToRight(fromDxgi);

        return DisplayLayout.NumberLeftToRight(FromWindows());
    }

    /// <summary>
    /// The one line the first real two-screen machine has to produce. Written into the diagnostics
    /// report and the capture log so the answer arrives as evidence rather than as a question.
    /// </summary>
    internal static string Describe(IReadOnlyList<DisplayInfo> displays, int capturedNumber)
    {
        if (displays.Count == 0) return "none could be enumerated";

        var bounds = DisplayLayout.BoundsOf(displays);
        var parts = displays.Select(d =>
            $"#{d.Number} {d.Width}x{d.Height} at ({d.X},{d.Y}){(d.IsPrimary ? " primary" : "")}" +
            $"{(d.Number == capturedNumber ? " <= CAPTURING" : "")}");

        return $"{displays.Count} found — {string.Join(" · ", parts)}. " +
               $"Whole desktop {bounds.Width}x{bounds.Height} from ({bounds.Left},{bounds.Top}).";
    }

    private static List<DisplayInfo> TryDxgi()
    {
        var found = new List<DisplayInfo>();
        try
        {
            D3D11.D3D11CreateDevice(
                null!, Vortice.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 },
                out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();

            using var d = device!;
            using var c = context!;
            using var dxgiDevice = d.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();

            for (int i = 0; ; i++)
            {
                // EnumOutputs fails rather than returning null once the list is exhausted, so the
                // loop ends on the first failure rather than on a count nobody supplies.
                if (adapter.EnumOutputs((uint)i, out IDXGIOutput? output).Failure || output is null) break;
                using var o = output;

                var desc = o.Description;
                var r = desc.DesktopCoordinates;

                found.Add(new DisplayInfo(
                    Number: 0, // assigned by position afterwards
                    Name: desc.DeviceName ?? $"output {i}",
                    X: r.Left,
                    Y: r.Top,
                    Width: r.Right - r.Left,
                    Height: r.Bottom - r.Top,
                    // DXGI does not say which is primary. The primary is the one whose top-left is
                    // the virtual desktop origin, by definition of how Windows lays them out.
                    IsPrimary: r.Left == 0 && r.Top == 0));
            }
        }
        catch
        {
            // A machine without DXGI is the GDI fallback's whole reason for existing. Not an error.
            return new List<DisplayInfo>();
        }
        return found;
    }

    private static List<DisplayInfo> FromWindows()
    {
        var found = new List<DisplayInfo>();
        try
        {
            foreach (var s in Screen.AllScreens)
            {
                found.Add(new DisplayInfo(
                    Number: 0,
                    Name: s.DeviceName,
                    X: s.Bounds.X,
                    Y: s.Bounds.Y,
                    Width: s.Bounds.Width,
                    Height: s.Bounds.Height,
                    IsPrimary: s.Primary));
            }
        }
        catch
        {
            // Nothing to report is better than throwing out of an enumeration.
        }
        return found;
    }
}
