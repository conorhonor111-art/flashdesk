using System.Runtime.InteropServices;
using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.Viewer.Rendering;

namespace RemoteDesktop.Viewer.Input;

/// <summary>
/// Captures mouse and keyboard from the video control and forwards them to the host as InputEvents —
/// but only while control is <see cref="Enabled"/> and the control has focus, so the operator's own
/// machine is never driven by accident. Mouse positions are mapped to host pixels here (via the
/// canvas); keys are converted to hardware scan codes so the host's layout, not the viewer's Georgian
/// one, decides the character.
///
/// Tracks which keys and buttons it has pressed so it can release them — <see cref="ReleaseHeld"/> —
/// whenever control is switched off, the picture loses focus, or the session ends. Without that, an
/// operator who holds Shift and then clicks away would strand Shift "down" on the host.
/// </summary>
public sealed class InputCapture
{
    private readonly ScreenCanvas _canvas;
    private readonly Action<InputEvent> _send;
    private readonly Dictionary<ushort, bool> _downKeys = new();   // scan code -> extended flag
    private readonly HashSet<MouseButton> _downButtons = new();
    private int _lastX, _lastY;
    private bool _enabled;

    /// <summary>When set false, held keys and buttons are released first, so nothing is left stuck down.</summary>
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value) ReleaseHeld(); }
    }

    public InputCapture(ScreenCanvas canvas, Action<InputEvent> send)
    {
        _canvas = canvas;
        _send = send;

        _canvas.MouseMove += (_, e) => { if (Active && Map(e, out int x, out int y)) { _lastX = x; _lastY = y; _send(InputEvent.MouseMove(x, y)); } };
        _canvas.MouseDown += (_, e) => SendButton(e, true);
        _canvas.MouseUp += (_, e) => SendButton(e, false);
        _canvas.MouseWheel += (_, e) => { if (Active && Map(e, out int x, out int y)) _send(InputEvent.MouseWheel(e.Delta, x, y)); };
        _canvas.KeyDown += (_, e) => { if (Active) { SendKey(e.KeyCode, true); e.Handled = true; e.SuppressKeyPress = true; } };
        _canvas.KeyUp += (_, e) => { if (Active) { SendKey(e.KeyCode, false); e.Handled = true; } };

        // Losing focus while a key or button is held would otherwise strand it "down" on the host.
        _canvas.LostFocus += (_, _) => ReleaseHeld();
    }

    private bool Active => _enabled && _canvas.Focused;

    private bool Map(MouseEventArgs e, out int x, out int y) => _canvas.TryMapToHost(e.Location, out x, out y);

    private void SendButton(MouseEventArgs e, bool down)
    {
        if (!Active) return;
        var button = e.Button switch
        {
            MouseButtons.Left => MouseButton.Left,
            MouseButtons.Right => MouseButton.Right,
            MouseButtons.Middle => MouseButton.Middle,
            _ => MouseButton.None,
        };
        if (button == MouseButton.None) return;
        if (!Map(e, out int x, out int y)) return;

        _lastX = x;
        _lastY = y;
        if (down) _downButtons.Add(button); else _downButtons.Remove(button);
        _send(InputEvent.MouseButtonEvent(button, down, x, y));
    }

    private void SendKey(Keys key, bool down)
    {
        ushort scan = (ushort)MapVirtualKey((uint)key, MAPVK_VK_TO_VSC);
        if (scan == 0) return;

        bool extended = IsExtended(key);
        if (down) _downKeys[scan] = extended; else _downKeys.Remove(scan);
        _send(InputEvent.Key(scan, down, extended));
    }

    // Release every key and button still held, then forget them.
    private void ReleaseHeld()
    {
        if (_downKeys.Count > 0)
        {
            foreach (var (scan, extended) in _downKeys)
                _send(InputEvent.Key(scan, false, extended));
            _downKeys.Clear();
        }

        if (_downButtons.Count > 0)
        {
            foreach (var button in _downButtons)
                _send(InputEvent.MouseButtonEvent(button, false, _lastX, _lastY));
            _downButtons.Clear();
        }
    }

    // Keys whose scan code must carry the extended (0xE0) prefix to mean the right physical key.
    private static bool IsExtended(Keys key) => key switch
    {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Insert or Keys.Delete => true,
        Keys.NumLock or Keys.PrintScreen or Keys.Divide => true,
        Keys.LWin or Keys.RWin or Keys.Apps => true,
        Keys.RControlKey or Keys.RMenu => true,
        _ => false,
    };

    private const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
