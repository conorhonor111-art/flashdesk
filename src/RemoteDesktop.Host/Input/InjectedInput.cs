using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoteDesktop.Host.Input;

/// <summary>
/// Tells FlashDesk's own injected mouse and keyboard events apart from the hand of the person
/// sitting at this computer.
///
/// WHY THIS EXISTS. Until 2026-08-06 every event this program injected carried
/// <c>dwExtraInfo = 0</c> — byte for byte identical to a real click. Windows could not tell them
/// apart and neither could we. That is tolerable while the only consent dialog appears BEFORE a
/// session starts, because there is no remote hand yet. It stops being tolerable the moment any
/// dialog can appear DURING a session: the operator's own remote mouse could press the button and
/// the person being helped would never know it had been pressed for them. A consent nobody gave,
/// with a record saying they did, is worse than no consent at all.
///
/// CLAUDE.md already required this, in the allow-list reasoning: a confirmation that grants access
/// "must IGNORE INJECTED INPUT (the host knows which input it injected itself; the remote hand
/// cannot sign its own permission slip)". This is that mechanism.
///
/// HOW IT WORKS. Every INPUT structure SendInput sends is stamped with <see cref="Tag"/>. Windows
/// carries that value through to the receiving window, where <c>GetMessageExtraInfo</c> returns it
/// for the message currently being processed. A window that must only listen to the real hand
/// checks it and drops anything wearing the stamp.
///
/// HONEST LIMIT: this defends against OUR OWN injection, which is the whole attack it is meant to
/// stop. It is not a defence against other software on the machine synthesising input, and it never
/// could be — any program running as that user can call SendInput with any value it likes,
/// including this one. It stops the remote operator from answering a question put to the client.
/// </summary>
internal static class InjectedInput
{
    /// <summary>
    /// The marker carried by every event FlashDesk injects. "FLDS" as ASCII, chosen so that a value
    /// seen in a debugger is recognisable rather than looking like a stray pointer. Deliberately
    /// NOT zero (zero is what ordinary hardware input carries) and well clear of 0xFFFFFF00-
    /// 0xFFFFFFFF, which Windows reserves for its own injected-input signalling.
    /// </summary>
    internal static readonly IntPtr Tag = new(0x464C4453);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessageExtraInfo();

    /// <summary>
    /// True when the message this thread is currently handling was injected by FlashDesk itself.
    /// Only meaningful while dispatching an input message — that is the only time Windows has an
    /// extra-info value to return.
    /// </summary>
    internal static bool IsCurrentMessageInjected() => GetMessageExtraInfo() == Tag;

    /// <summary>
    /// Blocks FlashDesk's own injected mouse and keyboard input from reaching ANY window on this
    /// thread, for as long as it is registered. Register it while a window that must only listen to
    /// the real hand is open, and dispose it when that window closes.
    ///
    /// ⚠ IT MUST BE A MESSAGE FILTER, NOT A WndProc OVERRIDE ON THE DIALOG, and this was established
    /// by a test that FAILED. The first attempt overrode <c>WndProc</c> on the consent window and
    /// looked correct — it compiled, and the dialog behaved normally. Driving the real Accept button
    /// with a real <c>SendInput</c> click carrying the marker showed it accepted anyway: mouse
    /// messages for a click on a BUTTON are delivered to the BUTTON's window, so the form's WndProc
    /// never sees them. <see cref="IMessageFilter"/> runs in the message loop itself, before the
    /// message is dispatched to any window, which is the only place that sees every input message
    /// regardless of which control it was aimed at.
    /// </summary>
    internal sealed class Blocker : IMessageFilter, IDisposable
    {
        private const int WM_MOUSEFIRST = 0x0200, WM_MOUSELAST = 0x020E;
        private const int WM_KEYFIRST = 0x0100, WM_KEYLAST = 0x0108;

        internal Blocker() => Application.AddMessageFilter(this);

        public bool PreFilterMessage(ref Message m)
        {
            bool isInput = (m.Msg >= WM_MOUSEFIRST && m.Msg <= WM_MOUSELAST)
                        || (m.Msg >= WM_KEYFIRST && m.Msg <= WM_KEYLAST);

            // Swallowed in silence. A message saying "that click was blocked" would tell the
            // operator exactly what they had just tried and failed to do, and the person this
            // window belongs to has no reason to see an error for something they did not do.
            return isInput && IsCurrentMessageInjected();
        }

        public void Dispose() => Application.RemoveMessageFilter(this);
    }
}
