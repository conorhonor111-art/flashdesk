using RemoteDesktop.Shared.Files;
using RemoteDesktop.Shared.Protocol;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// Chunk sizing, tested against the thing it is FOR — the time one chunk holds the send lock — and
/// not against its own arithmetic. Asserting that a formula computes the formula proves nothing;
/// what has to hold is that a real home uplink never ends up with a chunk that takes long enough to
/// push the round trip past the point where the bandwidth governor drops the picture.
/// </summary>
public class FileChunkSizeTests
{
    /// <summary>
    /// Real uplinks, in true bytes per second. 0.6 MB/s is the figure CLAUDE.md uses for a slow home
    /// connection; the rest bracket it.
    /// </summary>
    [Theory]
    [InlineData(150_000)]    // a poor connection
    [InlineData(600_000)]    // the slow-home-uplink case the governor was built against
    [InlineData(1_200_000)]
    [InlineData(2_500_000)]  // a good home upload
    public void A_chunk_never_holds_the_send_lock_past_the_budget(double trueRate)
    {
        // What the governor would REPORT for this link: it reads 2-3x high, and 3x is the worst end.
        double reported = trueRate * FileChunkSize.RateOverstatement;

        int chunk = FileChunkSize.ForMeasuredRate(reported);
        double holdMs = FileChunkSize.HoldMsAt(chunk, trueRate);

        Assert.True(holdMs <= ProtocolConstants.MaxChunkSendHoldMs,
            $"{chunk} bytes on a {trueRate:0} B/s link holds the lock {holdMs:0} ms, over the {ProtocolConstants.MaxChunkSendHoldMs} ms budget.");
    }

    [Fact]
    public void The_fixed_256_KB_chunk_is_what_this_replaces_and_it_blew_the_budget()
    {
        // The measurement that justifies the whole class: the old fixed chunk on a 0.6 MB/s uplink.
        // Well past the governor's 250 ms queue threshold, so a download used to blur the screen.
        double hold = FileChunkSize.HoldMsAt(ProtocolConstants.MaxFileChunkBytes, 600_000);

        Assert.True(hold > 400);
        Assert.True(FileChunkSize.HoldMsAt(FileChunkSize.ForMeasuredRate(600_000 * 3), 600_000) < hold / 2);
    }

    [Fact]
    public void An_unmeasured_link_gets_the_cautious_default_not_the_ceiling()
    {
        // The first chunks of every transfer are sent in ignorance. Starting at the ceiling would
        // make exactly those the ones most likely to stall the picture.
        Assert.Equal(ProtocolConstants.UnmeasuredFileChunkBytes, FileChunkSize.ForMeasuredRate(0));
        Assert.True(ProtocolConstants.UnmeasuredFileChunkBytes < ProtocolConstants.MaxFileChunkBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Nonsense_readings_fall_back_rather_than_produce_a_nonsense_chunk(double rate) =>
        Assert.Equal(ProtocolConstants.UnmeasuredFileChunkBytes, FileChunkSize.ForMeasuredRate(rate));

    [Theory]
    [InlineData(1)]                 // absurdly slow
    [InlineData(1_000)]
    [InlineData(1_000_000_000)]     // a local loopback
    [InlineData(double.MaxValue)]   // the overflow row: large must not come out negative
    [InlineData(double.PositiveInfinity)]
    public void The_answer_always_stays_between_the_floor_and_the_ceiling(double rate)
    {
        int chunk = FileChunkSize.ForMeasuredRate(rate);
        Assert.InRange(chunk, ProtocolConstants.MinFileChunkBytes, ProtocolConstants.MaxFileChunkBytes);
    }

    [Fact]
    public void A_faster_link_never_gets_a_smaller_chunk()
    {
        int previous = 0;
        foreach (double rate in new double[] { 50_000, 200_000, 800_000, 3_000_000, 20_000_000 })
        {
            int chunk = FileChunkSize.ForMeasuredRate(rate);
            Assert.True(chunk >= previous, $"{rate:0} B/s produced {chunk}, smaller than the slower link's {previous}.");
            previous = chunk;
        }
    }

    [Fact]
    public void A_fast_link_does_reach_the_ceiling_so_a_LAN_transfer_is_not_crippled()
    {
        // The clamp has to bite at both ends. On a link that can carry it, the chunk is the ceiling
        // and the transfer runs at full speed.
        Assert.Equal(ProtocolConstants.MaxFileChunkBytes, FileChunkSize.ForMeasuredRate(50_000_000));
    }

    [Fact]
    public void The_overstatement_is_applied_and_not_quietly_dropped()
    {
        // If the divisor were ever removed, this is the row that fails: taking the reported rate at
        // face value would give three times the chunk, which is the original bug wearing new code.
        int believing = FileChunkSize.ForMeasuredRate(900_000);
        int naive = (int)(900_000 * (ProtocolConstants.MaxChunkSendHoldMs / 1000.0));

        Assert.True(believing < naive);
        Assert.Equal(naive / FileChunkSize.RateOverstatement, believing, 0);
    }

    [Fact]
    public void The_budget_stays_under_the_governors_queue_threshold()
    {
        // The coupling stated in both ProtocolConstants and BandwidthGovernor, pinned here so a
        // later edit to either number cannot silently break the reason this budget has its value.
        // 250 is BandwidthGovernor.QueueHeavyMs, which lives in the Host project and cannot be
        // referenced from a net8.0 test assembly.
        Assert.True(ProtocolConstants.MaxChunkSendHoldMs < 250);
    }
}
