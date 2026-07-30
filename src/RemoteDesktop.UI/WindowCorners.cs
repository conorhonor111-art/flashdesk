using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoteDesktop.UI;

/// <summary>
/// Asks the OS to round the window's outer frame. Windows 11 honours the request at the
/// compositor level for free; Windows 10 does not know the attribute and returns an error, which
/// is deliberately ignored — the frame simply stays square there.
/// </summary>
public static class WindowCorners
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Form form)
    {
        try
        {
            int preference = DwmwcpRound;
            _ = DwmSetWindowAttribute(form.Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
