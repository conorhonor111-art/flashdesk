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
/// </summary>
public sealed class InputCapture
{
    private readonly ScreenCanvas _canvas;
    private readonly Action<InputEvent> _send;

    /// <summary>When false, nothing is sent no matter what the mouse and keyboard do.</summary>
    public bool Enabled { get; set; }

    public InputCapture(ScreenCanvas canvas, Action<InputEvent> send)
    {
        _canvas = canvas;
        _send = send;

        _canvas.MouseMove += (_, e) => { if (Active && Map(e, out int x, out int y)) _send(InputEvent.MouseMove(x, y)); };
        _canvas.MouseDown += (_, e) => SendButton(e, true);
        _canvas.MouseUp += (_, e) => SendButton(e, false);
        _canvas.MouseWheel += (_, e) => { if (Active && Map(e, out int x, out int y)) _send(InputEvent.MouseWheel(e.Delta, x, y)); };
        _canvas.KeyDown += (_, e) => { if (Active) { SendKey(e.KeyCode, true); e.Handled = true; e.SuppressKeyPress = true; } };
        _canvas.KeyUp += (_, e) => { if (Active) { SendKey(e.KeyCode, false); e.Handled = true; } };
    }

    private bool Active => Enabled && _canvas.Focused;

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
        if (Map(e, out int x, out int y))
            _send(InputEvent.MouseButtonEvent(button, down, x, y));
    }

    private void SendKey(Keys key, bool down)
    {
        ushort scan = (ushort)MapVirtualKey((uint)key, MAPVK_VK_TO_VSC);
        if (scan == 0) return;
        _send(InputEvent.Key(scan, down, IsExtended(key)));
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
