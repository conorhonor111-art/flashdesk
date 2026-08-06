using System.Runtime.InteropServices;
using RemoteDesktop.Shared.Displays;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host.Input;

/// <summary>
/// Injects mouse and keyboard events into the host with the Win32 <c>SendInput</c> function.
///
/// Why SendInput and not the older <c>mouse_event</c> / <c>keybd_event</c>: those two are formally
/// deprecated, they inject one event at a time (so a modifier-plus-key can be split by other input
/// arriving in between), and they do not integrate with the modern raw-input path the way SendInput
/// does. SendInput delivers a batch atomically and lets us send keys by hardware **scan code**, so
/// the host's keyboard layout — not the viewer's — decides which character a physical key produces.
///
/// The injector tracks which keys and buttons it has pressed, so <see cref="ReleaseAll"/> can let
/// them up if the connection drops mid-press. Otherwise the host is left with Ctrl/Shift/Alt stuck
/// down and is unusable until reboot.
/// </summary>
public sealed class InputInjector
{
    private int _screenWidth;
    private int _screenHeight;

    // Where the captured screen sits on the whole desktop, and how big that desktop is.
    // _desktop stays NULL on a single-screen machine, which keeps the original code path in
    // charge of the only case that has ever been tested on real hardware.
    private int _originX;
    private int _originY;
    private VirtualBounds? _desktop;
    private readonly object _gate = new();
    private readonly HashSet<ushort> _downScanCodes = new();
    private readonly HashSet<MouseButton> _downButtons = new();

    public InputInjector(int screenWidth, int screenHeight)
    {
        _screenWidth = Math.Max(1, screenWidth);
        _screenHeight = Math.Max(1, screenHeight);
    }

    /// <summary>Update the target screen size after a mid-session resolution change, so absolute mouse
    /// coordinates keep mapping correctly.</summary>
    public void SetScreenSize(int width, int height)
    {
        _screenWidth = Math.Max(1, width);
        _screenHeight = Math.Max(1, height);
    }

    /// <summary>
    /// Tell the injector which screen is being captured and how the whole desktop is laid out.
    ///
    /// <para>Called whenever the captured display changes, and whenever the display set changes.
    /// Until it is called, the injector behaves exactly as it did before multi-monitor existed:
    /// origin (0,0) and a desktop the size of the captured screen — which is correct for the
    /// single-screen machine that is the only kind this has ever run on.</para>
    /// </summary>
    public void SetDisplayLayout(int originX, int originY, VirtualBounds desktop)
    {
        _originX = originX;
        _originY = originY;
        _desktop = desktop.Width > 0 && desktop.Height > 0 ? desktop : null;
    }

    /// <summary>Inject one event that arrived from the viewer.</summary>
    public void Apply(InputEvent e)
    {
        switch (e.Kind)
        {
            case InputEventKind.MouseMove:
                MoveMouse(e.X, e.Y);
                break;
            case InputEventKind.MouseButton:
                MoveMouse(e.X, e.Y);
                PressButton(e.Button, e.IsDown);
                break;
            case InputEventKind.MouseWheel:
                MoveMouse(e.X, e.Y);
                Send(NewMouse(0, 0, (uint)e.WheelDelta, MOUSEEVENTF_WHEEL));
                break;
            case InputEventKind.Key:
                Key(e.ScanCode, e.IsDown, e.Extended);
                break;
        }
    }

    /// <summary>Current mouse position in host screen pixels, and whether it is on the captured
    /// (primary) screen — sent to the viewer so it can draw the remote pointer.</summary>
    public (int X, int Y, bool OnScreen) GetCursor()
    {
        if (GetCursorPos(out POINT p))
            return (p.X, p.Y, p.X >= 0 && p.Y >= 0 && p.X < _screenWidth && p.Y < _screenHeight);
        return (-1, -1, false);
    }

    /// <summary>Let up every key and button still held, plus the standard modifiers for good measure.
    /// Call on viewer disconnect, on timeout and on shutdown.</summary>
    public void ReleaseAll()
    {
        ushort[] scans;
        MouseButton[] buttons;
        lock (_gate)
        {
            scans = _downScanCodes.ToArray();
            buttons = _downButtons.ToArray();
            _downScanCodes.Clear();
            _downButtons.Clear();
        }

        foreach (var scan in scans)
            Send(NewKey(scan, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP));

        // Even if tracking missed one, force the usual modifiers up.
        foreach (var scan in ModifierScanCodes)
            Send(NewKey(scan, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP));

        foreach (var button in buttons)
            PressButton(button, false);
    }

    private void MoveMouse(int hostX, int hostY)
    {
        // SINGLE SCREEN — unchanged, and it stays the default so nothing that works today can be
        // broken by code that has never run on real hardware. Absolute coordinates run 0..65535
        // across the PRIMARY screen, which is the whole desktop when there is only one.
        if (_desktop is not VirtualBounds desktop)
        {
            int nx0 = (int)(hostX * 65535L / Math.Max(1, _screenWidth - 1));
            int ny0 = (int)(hostY * 65535L / Math.Max(1, _screenHeight - 1));
            Send(NewMouse(nx0, ny0, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE));
            return;
        }

        // MORE THAN ONE SCREEN. Two things change together, and one without the other is worse than
        // neither:
        //   1. the pixel is turned into a point on the WHOLE desktop by adding the captured
        //      screen's origin, which is negative for a screen left of or above the primary;
        //   2. MOUSEEVENTF_VIRTUALDESK is set, because without it Windows spreads 0..65535 across
        //      the PRIMARY SCREEN ONLY — so every coordinate computed in step 1 would be squeezed
        //      back onto screen one, and the feature would look like it worked while sending every
        //      click to the wrong place.
        var (vx, vy) = DisplayLayout.ToVirtual(
            new DisplayInfo(0, "", _originX, _originY, _screenWidth, _screenHeight, false), hostX, hostY);
        var (nx, ny) = DisplayLayout.ToAbsolute(desktop, vx, vy);

        Send(NewMouse(nx, ny, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK));
    }

    private void PressButton(MouseButton button, bool down)
    {
        uint flag = button switch
        {
            MouseButton.Left => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
            MouseButton.Right => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            MouseButton.Middle => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => 0u,
        };
        if (flag == 0) return;
        Send(NewMouse(0, 0, 0, flag));
        lock (_gate)
        {
            if (down) _downButtons.Add(button);
            else _downButtons.Remove(button);
        }
    }

    private void Key(ushort scanCode, bool down, bool extended)
    {
        uint flags = KEYEVENTF_SCANCODE;
        if (!down) flags |= KEYEVENTF_KEYUP;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        Send(NewKey(scanCode, flags));
        lock (_gate)
        {
            if (down) _downScanCodes.Add(scanCode);
            else _downScanCodes.Remove(scanCode);
        }
    }

    private static readonly ushort[] ModifierScanCodes =
    {
        0x2A, // Left Shift
        0x36, // Right Shift
        0x1D, // Ctrl
        0x38, // Alt
        0x5B, // Left Windows
        0x5C, // Right Windows
    };

    // --- Win32 plumbing ---

    private static void Send(INPUT input) => SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

    // ⚠ EVERY injected event is stamped with InjectedInput.Tag, and the stamp is applied HERE, in
    // the two constructors, rather than in Send() — so an event cannot be built without it. Before
    // 2026-08-06 dwExtraInfo was left at zero, which made the remote hand indistinguishable from
    // the real one; a mid-session consent dialog could then be answered by the operator on the
    // client's behalf. See InjectedInput for the full reasoning.
    //
    // dwExtraInfo sits at a DIFFERENT offset in MOUSEINPUT and KEYBDINPUT, so it must be set on the
    // right member of the union — setting "the union's" copy would write to the wrong field for one
    // of the two.
    private static INPUT NewMouse(int dx, int dy, uint data, uint flags) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dx = dx,
                dy = dy,
                mouseData = data,
                dwFlags = flags,
                dwExtraInfo = InjectedInput.Tag,
            },
        },
    };

    private static INPUT NewKey(ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = scan,
                dwFlags = flags,
                dwExtraInfo = InjectedInput.Tag,
            },
        },
    };

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_ABSOLUTE = 0x8000;
    // Spreads the 0..65535 range across EVERY screen instead of only the primary. Without it
    // multi-monitor coordinates are silently squeezed onto screen one.
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
