using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.Viewer.Input;
using RemoteDesktop.Viewer.Rendering;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// Suspending remote control while the operator uses the file panel.
///
/// <para><b>⚠ WHAT THESE TESTS DO NOT COVER, said plainly: the release ORDER.</b> The invariant that
/// matters most — that every held key is released on the remote machine BEFORE forwarding stops, so
/// no modifier is stranded down — cannot be reached from here. Getting a key into the "held" state
/// requires the video control to genuinely have Windows keyboard focus, which needs a real shown
/// window and a real focus change; a test that faked it would be testing the fake. That invariant is
/// verified by reading <see cref="InputCapture.Suspend"/> and by the two-machine test, and it is
/// listed as unverified in PROGRESS.md rather than quietly assumed.</para>
///
/// <para>What IS covered is the regression that would be easy to introduce and impossible to notice:
/// a suspension silently turning the operator's own "Control their mouse and keyboard" choice off
/// and leaving it off.</para>
/// </summary>
public class InputSuspendTests
{
    private static InputCapture New(out List<InputEvent> sent)
    {
        var events = new List<InputEvent>();
        sent = events;
        return new InputCapture(new ScreenCanvas(), events.Add);
    }

    [Fact]
    public void Suspending_does_not_touch_the_operators_own_control_choice()
    {
        // The bug this prevents: the panel suspends control, the operator clicks back on the
        // picture, and nothing works any more because Enabled was used as the suspension flag.
        var capture = New(out _);
        capture.Enabled = true;

        capture.Suspend();
        Assert.True(capture.Suspended);
        Assert.True(capture.Enabled);

        capture.Resume();
        Assert.False(capture.Suspended);
        Assert.True(capture.Enabled);
    }

    [Fact]
    public void Suspending_twice_is_harmless_and_resuming_without_suspending_is_too()
    {
        // Focus can bounce between two controls inside the panel, so both of these happen.
        var capture = New(out var sent);
        capture.Enabled = true;

        capture.Suspend();
        capture.Suspend();
        Assert.True(capture.Suspended);

        capture.Resume();
        capture.Resume();
        Assert.False(capture.Suspended);

        // Nothing was invented to send: with no key held, a suspension is silent on the wire.
        Assert.Empty(sent);
    }

    [Fact]
    public void A_suspended_capture_stays_suspended_when_control_is_turned_off_and_on()
    {
        var capture = New(out _);
        capture.Enabled = true;
        capture.Suspend();

        capture.Enabled = false;
        capture.Enabled = true;

        // Enabled and Suspended are two different facts and neither may quietly clear the other.
        Assert.True(capture.Suspended);
    }
}
