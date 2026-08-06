using System.Buffers.Binary;
using RemoteDesktop.Shared.Protocol;
using Xunit;

namespace RemoteDesktop.Tests;

// Protocol layer only — pure logic, no UI, no real network. These hold the wire format and its
// hardening in place so a later tidy-up cannot silently reintroduce, for example, the length prefix
// that once would have allocated 32 GB.
public class ProtocolTests
{
    // ---- Screen availability ----
    // These carry the sentence an operator reads when the other machine cannot see its own screen.
    // Getting them wrong reintroduces the failure they exist to prevent: a frozen picture with no
    // explanation, which everyone reads as a crash.

    [Fact]
    public void ScreenState_round_trips_when_unavailable()
    {
        var original = ScreenStatePayload.Unavailable();
        var back = ScreenStatePayload.FromBytes(original.ToBytes());
        Assert.False(back.Available);
        Assert.Equal(original.Words, back.Words);
        Assert.NotEmpty(back.Words); // silence with no sentence is the bug, not the fix
    }

    [Fact]
    public void ScreenState_round_trips_when_available()
    {
        var back = ScreenStatePayload.FromBytes(ScreenStatePayload.Available_().ToBytes());
        Assert.True(back.Available);
    }

    [Fact]
    public void ScreenState_survives_a_flag_with_no_words()
    {
        var back = ScreenStatePayload.FromBytes(new byte[] { 0 });
        Assert.False(back.Available);
        Assert.Equal(string.Empty, back.Words);
    }

    [Fact]
    public void ScreenState_rejects_an_empty_payload()
    {
        Assert.Throws<ArgumentException>(() => ScreenStatePayload.FromBytes(Array.Empty<byte>()));
    }

    [Fact]
    public void ScreenState_caps_absurdly_long_words()
    {
        var huge = new ScreenStatePayload(false, new string('x', 5000));
        var bytes = huge.ToBytes();
        Assert.True(bytes.Length <= 1 + ScreenStatePayload.MaxWordBytes);
        Assert.False(ScreenStatePayload.FromBytes(bytes).Available);
    }

    // ---- Handshake ----

    [Fact]
    public void Handshake_round_trips()
    {
        var original = Handshake.Create(PeerRole.Viewer);
        var back = Handshake.FromBytes(original.ToBytes());
        Assert.Equal(original, back);
        Assert.True(back.IsValid);
        Assert.Equal(PeerRole.Viewer, back.Role);
    }

    [Fact]
    public void Handshake_with_wrong_magic_is_invalid()
    {
        var bad = new Handshake(0xDEADBEEF, ProtocolConstants.ProtocolVersion, PeerRole.Host);
        Assert.False(bad.IsValid);
    }

    [Fact]
    public void Handshake_too_short_throws()
    {
        Assert.Throws<InvalidDataException>(() => Handshake.FromBytes(new byte[3]));
    }

    // ---- ScreenInfo ----

    [Fact]
    public void ScreenInfo_round_trips()
    {
        var original = new ScreenInfo(1920, 1080, ProtocolConstants.TileSize);
        var back = ScreenInfo.FromBytes(original.ToBytes());
        Assert.Equal(original, back);
        Assert.Equal(15, back.ColumnCount); // 1920 / 128
        Assert.Equal(9, back.RowCount);     // ceil(1080 / 128)
    }

    // ---- InputEvent ----

    [Fact]
    public void InputEvent_each_kind_round_trips()
    {
        AssertRoundTrips(InputEvent.MouseMove(640, 480));
        AssertRoundTrips(InputEvent.MouseButtonEvent(MouseButton.Right, true, 10, 20));
        AssertRoundTrips(InputEvent.MouseWheel(-120, 5, 6));
        AssertRoundTrips(InputEvent.Key(0x2A, true, extended: false));
        AssertRoundTrips(InputEvent.Key(0x48, false, extended: true));

        static void AssertRoundTrips(InputEvent e) => Assert.Equal(e, InputEvent.FromBytes(e.ToBytes()));
    }

    [Fact]
    public void InputEvent_too_short_throws()
    {
        Assert.Throws<InvalidDataException>(() => InputEvent.FromBytes(new byte[5]));
    }

    // ---- FramePacket ----

    [Fact]
    public void FramePacket_round_trips_with_tiles_and_cursor()
    {
        var tiles = new List<TileUpdate>
        {
            new(0, 0, new byte[] { 1, 2, 3 }),
            new(3, 4, new byte[] { 9, 9, 9, 9 }),
        };
        var original = new FramePacket(42, tiles, cursorX: 100, cursorY: 200, cursorVisible: true);

        var back = FramePacket.FromBytes(original.ToBytes());

        Assert.Equal(42, back.FrameNumber);
        Assert.Equal(100, back.CursorX);
        Assert.Equal(200, back.CursorY);
        Assert.True(back.CursorVisible);
        Assert.Equal(2, back.Tiles.Count);
        Assert.Equal(0, back.Tiles[0].Column);
        Assert.Equal(3, back.Tiles[1].Column);
        Assert.Equal(4, back.Tiles[1].Row);
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, back.Tiles[1].Jpeg);
    }

    [Fact]
    public void FramePacket_empty_round_trips()
    {
        var back = FramePacket.FromBytes(new FramePacket(1, new List<TileUpdate>()).ToBytes());
        Assert.Empty(back.Tiles);
    }

    [Fact]
    public void FramePacket_too_short_throws()
    {
        Assert.Throws<InvalidDataException>(() => FramePacket.FromBytes(new byte[10]));
    }

    [Fact]
    public void FramePacket_negative_tile_count_throws()
    {
        var bytes = new FramePacket(1, new List<TileUpdate>()).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(17), -1); // tile count field
        Assert.Throws<InvalidDataException>(() => FramePacket.FromBytes(bytes));
    }

    [Fact]
    public void FramePacket_oversized_tile_length_throws()
    {
        // A single-tile frame with the tile length overwritten to claim far more bytes than are present.
        var bytes = new FramePacket(1, new List<TileUpdate> { new(0, 0, new byte[] { 1 }) }).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(21 + 8), int.MaxValue); // len field of tile 0
        Assert.Throws<InvalidDataException>(() => FramePacket.FromBytes(bytes));
    }

    [Fact]
    public void FramePacket_truncated_tile_header_throws()
    {
        // Header claims one tile, but no tile bytes follow.
        var bytes = new byte[21];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(17), 1); // tile count = 1
        Assert.Throws<InvalidDataException>(() => FramePacket.FromBytes(bytes));
    }

    // ---- MessageChannel framing ----

    [Fact]
    public async Task MessageChannel_round_trips_a_message()
    {
        using var stream = new MemoryStream();
        var channel = new MessageChannel(stream);
        var payload = new byte[] { 10, 20, 30, 40 };

        await channel.SendAsync(MessageType.Frame, payload);
        stream.Position = 0;
        var received = await channel.ReceiveAsync();

        Assert.NotNull(received);
        Assert.Equal(MessageType.Frame, received!.Value.Type);
        Assert.Equal(payload, received.Value.Payload);
    }

    [Fact]
    public async Task MessageChannel_clean_close_returns_null()
    {
        using var stream = new MemoryStream(); // empty = closed at a message boundary
        var channel = new MessageChannel(stream);
        Assert.Null(await channel.ReceiveAsync());
    }

    [Fact]
    public async Task MessageChannel_truncated_header_throws()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 }); // fewer than the 5 header bytes
        var channel = new MessageChannel(stream);
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await channel.ReceiveAsync());
    }

    [Fact]
    public async Task MessageChannel_oversized_length_prefix_throws()
    {
        var header = new byte[5];
        header[0] = (byte)MessageType.Frame;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), int.MaxValue); // way over the cap
        using var stream = new MemoryStream(header);
        var channel = new MessageChannel(stream);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await channel.ReceiveAsync());
    }

    /// <summary>
    /// Pins the cap at 16 MB. The length prefix comes from the other end of the wire, so this
    /// number is the most memory a hostile peer can make us allocate from one five-byte header.
    /// It sat at 64 MB long after CLAUDE.md required 16, because nothing failed when it did not
    /// change — so the boundary is now asserted rather than trusted, and raising it silently
    /// breaks this test.
    /// </summary>
    [Fact]
    public async Task MessageChannel_rejects_a_length_just_over_sixteen_megabytes()
    {
        var header = new byte[5];
        header[0] = (byte)MessageType.Frame;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), (16 * 1024 * 1024) + 1);
        using var stream = new MemoryStream(header);
        var channel = new MessageChannel(stream);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await channel.ReceiveAsync());
    }

    /// <summary>
    /// The other half of the boundary: a length of exactly 16 MB must pass the range check. It then
    /// fails on the truncated body with a DIFFERENT exception, and that difference is the proof —
    /// InvalidDataException would mean the cap had rejected it, which would make the limit 16 MB
    /// minus one and leave the real boundary somewhere nobody had checked.
    /// </summary>
    [Fact]
    public async Task MessageChannel_accepts_a_length_of_exactly_sixteen_megabytes()
    {
        var header = new byte[5];
        header[0] = (byte)MessageType.Frame;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), 16 * 1024 * 1024);
        using var stream = new MemoryStream(header); // header only: the body is not there
        var channel = new MessageChannel(stream);
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await channel.ReceiveAsync());
    }
}
