using System.Buffers.Binary;
using System.Text;
using RemoteDesktop.Shared.Files;
using RemoteDesktop.Shared.Protocol;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The file messages, tested the way the rest of the protocol is: every message must survive a
/// round trip, and every length that arrives from the wire must be refused before anything is
/// allocated on the strength of it.
///
/// The hostile rows matter more than the round trips. This protocol has already had one length
/// prefix that would have allocated 32 GB, and file transfer adds eight new places for the same
/// mistake — a path length, a message length, an entry count, a chunk length. Each one is a number
/// the other end chooses.
/// </summary>
public class FileMessageTests
{
    // ------------------------------------------------------------------ round trips

    [Fact]
    public void FileAccessRequest_round_trips()
    {
        var back = FileAccessRequest.FromBytes(new FileAccessRequest(7).ToBytes());
        Assert.Equal(7, back.RequestId);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "They pressed Refuse.")]
    public void FileAccessReply_round_trips(bool granted, string reason)
    {
        var back = FileAccessReply.FromBytes(new FileAccessReply(3, granted, reason).ToBytes());
        Assert.Equal(3, back.RequestId);
        Assert.Equal(granted, back.Granted);
        Assert.Equal(reason, back.Reason);
    }

    [Theory]
    [InlineData(@"C:\Users\Public")]
    [InlineData(@"C:\Пользователи\Документы")]
    [InlineData(@"C:\მომხმარებელი")]
    [InlineData("")] // empty means "list the drives" - the root of the browser
    public void DirListRequest_round_trips_including_non_ascii(string path)
    {
        var back = DirListRequest.FromBytes(new DirListRequest(11, path, 0).ToBytes());
        Assert.Equal(11, back.RequestId);
        Assert.Equal(path, back.Path);
    }

    [Fact]
    public void DirListReply_round_trips_with_entries()
    {
        var entries = new List<DirEntry>
        {
            new("Documents", 0, true, 638_000_000_000_000_000L),
            new("Invoice März.pdf", 2_411_724, false, 638_100_000_000_000_000L),
            new("empty.txt", 0, false, 0),
        };
        var back = DirListReply.FromBytes(new DirListReply(5, FileStatus.Ok, "", 0, false, entries).ToBytes());

        Assert.Equal(5, back.RequestId);
        Assert.Equal(FileStatus.Ok, back.Status);
        Assert.Equal(3, back.Entries.Count);
        Assert.Equal("Invoice März.pdf", back.Entries[1].Name);
        Assert.Equal(2_411_724, back.Entries[1].Size);
        Assert.False(back.Entries[1].IsDirectory);
        Assert.True(back.Entries[0].IsDirectory);
    }

    [Fact]
    public void DirListReply_round_trips_an_empty_folder()
    {
        // An empty folder and a failed listing must not look the same on the wire: this one is Ok
        // with no rows, and the operator is told the folder is empty rather than that something
        // went wrong.
        var back = DirListReply.FromBytes(
            new DirListReply(1, FileStatus.Ok, "", 0, false, Array.Empty<DirEntry>()).ToBytes());
        Assert.Equal(FileStatus.Ok, back.Status);
        Assert.Empty(back.Entries);
    }

    [Fact]
    public void DirListReply_carries_a_refusal_with_its_sentence()
    {
        var back = DirListReply.FromBytes(new DirListReply(
            2, FileStatus.RefusedByPerson, "They did not allow file access.", 0, false, Array.Empty<DirEntry>()).ToBytes());
        Assert.Equal(FileStatus.RefusedByPerson, back.Status);
        Assert.Equal("They did not allow file access.", back.Message);
    }

    [Fact]
    public void FileGetRequest_round_trips()
    {
        var back = FileGetRequest.FromBytes(new FileGetRequest(9, @"C:\a\b.txt", 0).ToBytes());
        Assert.Equal(@"C:\a\b.txt", back.Path);
        Assert.Equal(0, back.StartOffset);
    }

    [Fact]
    public void FileChunk_round_trips_including_a_one_byte_file()
    {
        var back = FileChunk.FromBytes(new FileChunk(4, 0, new byte[] { 0x42 }).ToBytes());
        Assert.Equal(4, back.RequestId);
        Assert.Equal(0, back.Offset);
        Assert.Equal(new byte[] { 0x42 }, back.Bytes);
    }

    [Fact]
    public void FileGetEnd_round_trips()
    {
        var back = FileGetEnd.FromBytes(
            new FileGetEnd(6, FileStatus.InUse, 0, "That file is in use and could not be read.").ToBytes());
        Assert.Equal(FileStatus.InUse, back.Status);
        Assert.Equal("That file is in use and could not be read.", back.Message);
    }

    [Fact]
    public void FileGetCancel_round_trips()
        => Assert.Equal(8, FileGetCancel.FromBytes(new FileGetCancel(8).ToBytes()).RequestId);

    // ------------------------------------------------------------------ paging

    /// <summary>
    /// C:\Windows\WinSxS holds six figures of entries on an ordinary machine. One message for all
    /// of them would build a payload of megabytes, hold the send lock long enough to delay the
    /// latency ping behind it, and with long names could reach the 16 MB channel ceiling and drop
    /// the connection outright. So a listing is a window, and the window position round-trips.
    /// </summary>
    [Fact]
    public void A_listing_carries_its_position_and_whether_more_follows()
    {
        var page = new DirListReply(3, FileStatus.Ok, "", 2000, true,
            new List<DirEntry> { new("a.txt", 1, false, 0) });
        var back = DirListReply.FromBytes(page.ToBytes());

        Assert.Equal(2000, back.Skip);
        Assert.True(back.HasMore);
    }

    [Fact]
    public void The_last_page_says_nothing_follows()
    {
        var back = DirListReply.FromBytes(
            new DirListReply(3, FileStatus.Ok, "", 99_000, false, Array.Empty<DirEntry>()).ToBytes());
        Assert.False(back.HasMore);
    }

    [Fact]
    public void A_request_round_trips_its_page_position()
    {
        var back = DirListRequest.FromBytes(new DirListRequest(1, @"C:\Windows\WinSxS", 5000).ToBytes());
        Assert.Equal(5000, back.Skip);
    }

    [Fact]
    public void A_negative_page_position_throws()
    {
        // There is no legitimate sender that produces one, and a negative skip would mean an
        // unbounded backward walk through the enumeration.
        var bytes = new DirListRequest(1, @"C:\x", 0).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), -5);
        Assert.Throws<InvalidDataException>(() => DirListRequest.FromBytes(bytes));
    }

    [Fact]
    public void A_full_page_of_long_names_stays_far_under_the_channel_ceiling()
    {
        // The number that matters: the worst realistic page must not approach 16 MB, or a listing
        // could drop the connection instead of showing a folder.
        var entries = new List<DirEntry>();
        for (int i = 0; i < DirListReply.PageSize; i++)
            entries.Add(new DirEntry(new string('n', 200) + i, long.MaxValue, false, long.MaxValue));

        byte[] bytes = new DirListReply(1, FileStatus.Ok, "", 0, true, entries).ToBytes();
        Assert.True(bytes.Length < 1024 * 1024,
            $"a full page came to {bytes.Length} bytes, which is too close to the channel limit");
    }

    // ------------------------------------------------------------------ hostile input

    [Fact]
    public void A_string_claiming_more_bytes_than_are_present_throws()
    {
        // The shape of the original 32 GB bug: the number is plausible, the bytes are not there.
        var bytes = new DirListRequest(1, @"C:\x", 0).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 4000);
        Assert.Throws<InvalidDataException>(() => DirListRequest.FromBytes(bytes));
    }

    [Fact]
    public void A_negative_string_length_throws()
    {
        var bytes = new DirListRequest(1, @"C:\x", 0).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), -1);
        Assert.Throws<InvalidDataException>(() => DirListRequest.FromBytes(bytes));
    }

    [Fact]
    public void A_string_over_the_limit_throws_even_when_the_bytes_are_present()
    {
        // Both halves matter: a sender that really does supply 9 KB of path is still refused,
        // because the limit is the limit and not merely a description of what usually arrives.
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(1));                 // request id
        byte[] huge = Encoding.UTF8.GetBytes(new string('a', 9000));
        b.AddRange(BitConverter.GetBytes(huge.Length));
        b.AddRange(huge);
        Assert.Throws<InvalidDataException>(() => DirListRequest.FromBytes(b.ToArray()));
    }

    [Fact]
    public void An_entry_count_beyond_the_limit_throws()
    {
        var bytes = new DirListReply(1, FileStatus.Ok, "", 0, false, Array.Empty<DirEntry>()).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), 2_000_000);
        Assert.Throws<InvalidDataException>(() => DirListReply.FromBytes(bytes));
    }

    [Fact]
    public void An_entry_count_that_lies_about_what_follows_throws()
    {
        // Under the cap, so the count check passes — and then the bytes run out. This is the row
        // that proves the guard is the ACTUAL PRESENCE of bytes and not just a sensible-looking
        // number, which is the mistake that would leave a plausible count allocating for nothing.
        var bytes = new DirListReply(1, FileStatus.Ok, "", 0, false, Array.Empty<DirEntry>()).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), 500);
        Assert.Throws<InvalidDataException>(() => DirListReply.FromBytes(bytes));
    }

    [Fact]
    public void A_chunk_longer_than_the_limit_throws()
    {
        var bytes = new FileChunk(1, 0, new byte[] { 1, 2, 3 }).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), ProtocolConstants.MaxFileChunkBytes + 1);
        Assert.Throws<InvalidDataException>(() => FileChunk.FromBytes(bytes));
    }

    [Fact]
    public void A_chunk_longer_than_its_message_throws()
    {
        var bytes = new FileChunk(1, 0, new byte[] { 1, 2, 3 }).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 100_000); // under the cap, not present
        Assert.Throws<InvalidDataException>(() => FileChunk.FromBytes(bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    public void Every_message_refuses_a_truncated_body(int keep)
    {
        var full = new FileGetRequest(1, @"C:\a", 0).ToBytes();
        var cut = full.AsSpan(0, Math.Min(keep, full.Length)).ToArray();
        Assert.Throws<InvalidDataException>(() => FileGetRequest.FromBytes(cut));
    }

    // ------------------------------------------------------------------ the upload messages

    [Theory]
    [InlineData(@"C:\Users\Ann\Documents", "Invoice März.pdf")]
    [InlineData(@"C:\ProgramData\App", "config.ini")]
    [InlineData(@"C:\მომხმარებელი", "ანგარიში.pdf")]
    public void FileSendRequest_round_trips_folder_and_name_separately(string folder, string name)
    {
        // Separately, and that is the point: joining them at the sender would make the whole
        // destination one attacker-controlled string, and the containment check exists precisely to
        // keep the folder something the operator chose and the name something that cannot leave it.
        var back = FileSendRequest.FromBytes(new FileSendRequest(31, folder, name, 2_411_724).ToBytes());

        Assert.Equal(31, back.RequestId);
        Assert.Equal(folder, back.Folder);
        Assert.Equal(name, back.Name);
        Assert.Equal(2_411_724, back.TotalBytes);
    }

    [Fact]
    public void A_negative_declared_size_throws()
    {
        // Not a small file: a broken or hostile sender. The declared size is what the free-space
        // check and the question shown to the person are built on, so it is refused at the edge.
        var bytes = new FileSendRequest(1, @"C:\x", "a.txt", 0).ToBytes();
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(bytes.Length - 8), -1);
        Assert.Throws<InvalidDataException>(() => FileSendRequest.FromBytes(bytes));
    }

    [Fact]
    public void FileSendReply_carries_the_name_it_was_actually_saved_as()
    {
        var back = FileSendReply.FromBytes(
            new FileSendReply(31, FileStatus.Ok, "", "Report (2).docx").ToBytes());

        Assert.Equal(FileStatus.Ok, back.Status);
        Assert.Equal("Report (2).docx", back.SavedAs);
    }

    [Theory]
    [InlineData(FileStatus.NoRoom)]
    [InlineData(FileStatus.NameTaken)]
    [InlineData(FileStatus.WriteError)]
    [InlineData(FileStatus.RefusedByPerson)]
    public void FileSendResult_round_trips_every_way_an_upload_can_end(FileStatus status)
    {
        var back = FileSendResult.FromBytes(
            new FileSendResult(31, status, 1_048_576, "There is not enough room on that computer.").ToBytes());

        Assert.Equal(status, back.Status);
        Assert.Equal(1_048_576, back.WrittenBytes);
        Assert.Equal("There is not enough room on that computer.", back.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(20)]
    public void The_upload_messages_refuse_a_truncated_body(int keep)
    {
        var request = new FileSendRequest(1, @"C:\Users\Ann\Documents", "report.docx", 100).ToBytes();
        Assert.Throws<InvalidDataException>(() =>
            FileSendRequest.FromBytes(request.AsSpan(0, Math.Min(keep, request.Length)).ToArray()));

        var reply = new FileSendReply(1, FileStatus.Ok, "a message", "saved as").ToBytes();
        Assert.Throws<InvalidDataException>(() =>
            FileSendReply.FromBytes(reply.AsSpan(0, Math.Min(keep, reply.Length)).ToArray()));
    }

    [Fact]
    public void An_upload_name_claiming_more_bytes_than_are_present_throws()
    {
        // The same shape as the 32 GB bug, on the write side: the folder length is honest, the
        // name's is not.
        var bytes = new FileSendRequest(1, @"C:\x", "a.txt", 10).ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + 4 + 4), 4000); // the name's length
        Assert.Throws<InvalidDataException>(() => FileSendRequest.FromBytes(bytes));
    }

    [Fact]
    public void A_capability_a_peer_does_not_have_is_not_claimed_by_accident()
    {
        // Browsing and upload are separate bits because they are separate permissions. A build with
        // one must not read as having both.
        var browsingOnly = Handshake.FromBytes(
            Handshake.Create(PeerRole.Host, PeerCapabilities.FileBrowsing).ToBytes());

        Assert.True(browsingOnly.Can(PeerCapabilities.FileBrowsing));
        Assert.False(browsingOnly.Can(PeerCapabilities.FileUpload));

        var both = Handshake.FromBytes(
            Handshake.Create(PeerRole.Host, PeerCapabilities.FileBrowsing | PeerCapabilities.FileUpload).ToBytes());

        Assert.True(both.Can(PeerCapabilities.FileBrowsing));
        Assert.True(both.Can(PeerCapabilities.FileUpload));

        // A greeting from a build that predates capabilities claims nothing, and that is not an error.
        Assert.False(Handshake.FromBytes(new byte[] { 0x31, 0x4B, 0x44, 0x52, 1, 1 })
            .Can(PeerCapabilities.FileUpload));
    }
}
