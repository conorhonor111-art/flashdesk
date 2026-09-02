using System.Diagnostics;
using RemoteDesktop.Host.Net;
using Xunit;
using Xunit.Abstractions;

namespace RemoteDesktop.Tests;

/// <summary>
/// Proves the recovery fix from 2026-08-27: a steady, harmless trickle on the link — never bad
/// enough to step the level down again — used to block recovery forever, because the dead-zone
/// branch in <c>OnFrameSent</c> zeroed the quiet-window count exactly like a real overload did. This
/// runs the real wall clock (the governor's windows are timed off <c>Environment.TickCount64</c>,
/// not an injectable clock), so it is slower than the rest of the suite — a few tens of seconds, not
/// milliseconds. Kept to one test for that reason.
///
/// Per CLAUDE.md rule 11, this was run against the UNFIXED code first (the dead-zone branch reverted
/// to <c>_quietWindows = 0</c>) and confirmed to fail there before being trusted — see PROGRESS.md
/// for both runs' numbers. This file always contains the FIXED assertion; the unfixed run is not
/// preserved as a variant here because there is nothing to toggle at runtime — the behaviour being
/// proved is a one-line difference in production code, and the honest way to prove a test can fail is
/// to actually revert that line and watch it fail, not to fake the old behaviour inside the test.
/// </summary>
public class BandwidthGovernorRecoveryTests
{
    private readonly ITestOutputHelper _output;
    public BandwidthGovernorRecoveryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void A_steady_harmless_trickle_still_lets_the_level_climb()
    {
        var gov = new BandwidthGovernor();

        // Push it down to level 6 — the exact level the real stuck session was found at — using the
        // PANIC path, which reacts to one call and needs no real time between calls.
        gov.OnFrameSent(50_000, sendMs: 800, screenChanged: true, cursorMoved: false); // -> level 2
        gov.OnFrameSent(50_000, sendMs: 800, screenChanged: true, cursorMoved: false); // -> level 4
        gov.OnFrameSent(50_000, sendMs: 800, screenChanged: true, cursorMoved: false); // -> level 6
        int startLevel = gov.Level;
        Assert.Equal(6, startLevel);

        // A steady rhythm, mostly clean with one dead-zone window in every three — a harmless trickle,
        // never a real overload (it never crosses HeavyOverrun, so it never re-triggers StepDown).
        // Windows are timed off the real clock, so each call is spaced past DecisionWindowMs (500 ms,
        // private) with a safety margin for scheduler jitter.
        const int WindowSpacingMs = 650;
        const int MaxWindows = 40;

        var sw = Stopwatch.StartNew();
        int windowsRun = 0;
        for (; windowsRun < MaxWindows && gov.Level > 0; windowsRun++)
        {
            bool deadZoneTurn = windowsRun % 3 == 2; // 2 clean : 1 dead-zone, repeating
            if (deadZoneTurn)
                // Overrun lands inside (0.40, 0.85) at this level's frame interval — a real, if
                // small, cost, but never enough to cross HeavyOverrun. Bytes kept under the
                // "measurable" floor so this never touches the rate estimate or the over-budget
                // check — isolating exactly the dead-zone branch under test.
                gov.OnFrameSent(1_000, sendMs: 0.5 * gov.FrameIntervalMs, screenChanged: true, cursorMoved: false);
            else
                gov.OnFrameSent(100, sendMs: 1, screenChanged: true, cursorMoved: false);

            Thread.Sleep(WindowSpacingMs);
        }
        sw.Stop();

        _output.WriteLine($"Started at level {startLevel}/9. After {windowsRun} windows "
            + $"({sw.Elapsed.TotalSeconds:0.0}s of real time), level is {gov.Level}/9.");

        // The number Conor asked for: this must show real movement, not just "not stuck at exactly 6".
        Assert.True(gov.Level < startLevel,
            $"expected the level to climb from {startLevel} under a 2:1 clean-to-dead-zone rhythm, " +
            $"but it is still {gov.Level} after {windowsRun} windows ({sw.Elapsed.TotalSeconds:0.0}s)");
    }
}
