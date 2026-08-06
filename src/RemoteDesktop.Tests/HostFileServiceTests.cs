using System.Net;
using System.Net.Sockets;
using RemoteDesktop.Host.Files;
using RemoteDesktop.Host.Net;
using RemoteDesktop.Shared.Files;
using RemoteDesktop.Shared.Protocol;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The host's file service, driven the way a viewer drives it: real messages over a real loopback
/// socket, against real folders on a real disk.
///
/// <para><b>Why not a mock.</b> Everything worth proving here is a fact about the world — that a
/// listing really does refuse before consent, that a hundred thousand entries really do arrive in
/// pages, that a file's bytes really do come back identical, that Cancel really does stop a
/// transfer in flight. A fake channel and a fake file system would only prove the fakes agree with
/// the code.</para>
/// </summary>
public class HostFileServiceTests : IAsyncLifetime
{
    private string _folder = string.Empty;
    private TcpListener? _listener;
    private TcpClient? _hostSide;
    private TcpClient? _viewerSide;
    private MessageChannel? _hostChannel;
    private MessageChannel? _viewer;

    public async Task InitializeAsync()
    {
        _folder = Path.Combine(Path.GetTempPath(), "flashdesk-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var accepting = _listener.AcceptTcpClientAsync();
        _viewerSide = new TcpClient();
        await _viewerSide.ConnectAsync((IPEndPoint)_listener.LocalEndpoint);
        _hostSide = await accepting;

        _hostChannel = new MessageChannel(_hostSide.GetStream());
        _viewer = new MessageChannel(_viewerSide.GetStream());
    }

    public Task DisposeAsync()
    {
        _hostChannel?.Dispose();
        _viewer?.Dispose();
        _hostSide?.Dispose();
        _viewerSide?.Dispose();
        _listener?.Stop();
        try { Directory.Delete(_folder, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    /// <summary>Records what the service reported to the session log, so the log can be asserted.</summary>
    private readonly List<(string Caller, string Name, long Bytes, string Folder)> _logged = new();

    private HostFileService NewService(bool consent) => new(
        _hostChannel!,
        new BandwidthGovernor(),
        "418205793",
        _ => Task.FromResult(consent),
        (caller, name, bytes, folder) => { lock (_logged) _logged.Add((caller, name, bytes, folder)); });

    /// <summary>Pushes one message in the way the inbound loop does, then waits for the answer.</summary>
    private async Task<ReceivedMessage> ExchangeAsync(HostFileService service, MessageType type, byte[] payload)
    {
        Assert.True(service.TryHandle(type, payload, CancellationToken.None), $"{type} was not taken by the service.");
        return await NextAsync();
    }

    private async Task<ReceivedMessage> NextAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var message = await _viewer!.ReceiveAsync(timeout.Token);
        Assert.NotNull(message);
        return message!.Value;
    }

    private async Task<HostFileService> AllowedServiceAsync()
    {
        var service = NewService(consent: true);
        var reply = await ExchangeAsync(service, MessageType.FileAccessRequest, new FileAccessRequest(1).ToBytes());
        Assert.True(FileAccessReply.FromBytes(reply.Payload).Granted);
        return service;
    }

    // ------------------------------------------------------------------ consent

    [Fact]
    public async Task Nothing_is_listed_before_the_person_has_been_asked()
    {
        // The row that matters most: no consent has been given, so the disk is never touched.
        var service = NewService(consent: true);

        var reply = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest, new DirListRequest(1, _folder, 0).ToBytes())).Payload);

        Assert.Equal(FileStatus.NotAllowed, reply.Status);
        Assert.Empty(reply.Entries);
        Assert.NotEmpty(reply.Message); // a blank message reads as a crash
    }

    [Fact]
    public async Task Refusing_means_refused_and_it_does_not_get_asked_again()
    {
        int asked = 0;
        var service = new HostFileService(_hostChannel!, new BandwidthGovernor(), "418205793",
            _ => { Interlocked.Increment(ref asked); return Task.FromResult(false); }, null);

        var first = FileAccessReply.FromBytes(
            (await ExchangeAsync(service, MessageType.FileAccessRequest, new FileAccessRequest(1).ToBytes())).Payload);
        var second = FileAccessReply.FromBytes(
            (await ExchangeAsync(service, MessageType.FileAccessRequest, new FileAccessRequest(2).ToBytes())).Payload);

        Assert.False(first.Granted);
        Assert.False(second.Granted);
        // One question per connection. Re-asking is how a person is worn down into a yes.
        Assert.Equal(1, asked);

        var listing = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest, new DirListRequest(3, _folder, 0).ToBytes())).Payload);
        Assert.Equal(FileStatus.NotAllowed, listing.Status);
    }

    [Fact]
    public async Task A_missing_consent_callback_refuses_rather_than_allows()
    {
        // A wiring mistake must fail closed. This is the shape of the bug that would matter most.
        var service = new HostFileService(_hostChannel!, new BandwidthGovernor(), "418205793", null, null);

        var reply = FileAccessReply.FromBytes(
            (await ExchangeAsync(service, MessageType.FileAccessRequest, new FileAccessRequest(1).ToBytes())).Payload);

        Assert.False(reply.Granted);
    }

    // ------------------------------------------------------------------ listing

    [Fact]
    public async Task Folders_come_before_files_with_names_and_sizes()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Invoices"));
        Directory.CreateDirectory(Path.Combine(_folder, "Photos"));
        File.WriteAllBytes(Path.Combine(_folder, "notes.txt"), new byte[123]);
        File.WriteAllBytes(Path.Combine(_folder, "Invoice März.pdf"), new byte[4096]);

        var service = await AllowedServiceAsync();
        var reply = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest, new DirListRequest(2, _folder, 0).ToBytes())).Payload);

        Assert.Equal(FileStatus.Ok, reply.Status);
        Assert.Equal(4, reply.Entries.Count);
        Assert.False(reply.HasMore);

        Assert.True(reply.Entries[0].IsDirectory);
        Assert.True(reply.Entries[1].IsDirectory);
        Assert.False(reply.Entries[2].IsDirectory);
        Assert.False(reply.Entries[3].IsDirectory);

        var notes = reply.Entries.Single(e => e.Name == "notes.txt");
        Assert.Equal(123, notes.Size);
        Assert.True(notes.ModifiedUtcTicks > 0);

        // Non-ASCII names survive the round trip - a German or Georgian file name is ordinary.
        Assert.Contains(reply.Entries, e => e.Name == "Invoice März.pdf");
        // A folder's size is always 0 and means nothing.
        Assert.All(reply.Entries.Where(e => e.IsDirectory), e => Assert.Equal(0, e.Size));
    }

    [Fact]
    public async Task A_folder_bigger_than_one_page_arrives_in_pages_with_nothing_lost()
    {
        // WinSxS is the real case: six figures of entries. This is the same shape, small enough to
        // run in a test - the page boundary is what has to be right, not the size.
        const int total = DirListReply.PageSize + 5;
        for (int i = 0; i < total; i++)
            File.WriteAllBytes(Path.Combine(_folder, $"file-{i:0000}.bin"), Array.Empty<byte>());

        var service = await AllowedServiceAsync();

        var first = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest, new DirListRequest(2, _folder, 0).ToBytes())).Payload);
        Assert.Equal(DirListReply.PageSize, first.Entries.Count);
        Assert.True(first.HasMore);
        Assert.Equal(0, first.Skip);

        var second = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest,
                new DirListRequest(3, _folder, DirListReply.PageSize).ToBytes())).Payload);
        Assert.Equal(5, second.Entries.Count);
        Assert.False(second.HasMore);
        Assert.Equal(DirListReply.PageSize, second.Skip);

        // Every entry appears exactly once across the two pages.
        var names = first.Entries.Concat(second.Entries).Select(e => e.Name).ToList();
        Assert.Equal(total, names.Count);
        Assert.Equal(total, names.Distinct().Count());
    }

    [Fact]
    public async Task An_empty_path_lists_this_machines_own_drives()
    {
        var service = await AllowedServiceAsync();
        var reply = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest, new DirListRequest(2, "", 0).ToBytes())).Payload);

        Assert.Equal(FileStatus.Ok, reply.Status);
        Assert.NotEmpty(reply.Entries);
        Assert.All(reply.Entries, e => Assert.True(e.IsDirectory));
        // The NAME IS THE PATH, because the panel navigates by joining what it was given.
        Assert.All(reply.Entries, e => Assert.True(Path.IsPathFullyQualified(e.Name), $"'{e.Name}' is not a path."));
        Assert.Contains(reply.Entries, e => e.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(@"\\fileserver\finance")]     // another machine, spelled plainly
    [InlineData(@"\\?\C:\Windows")]           // the extended namespace, which bypasses normalisation
    [InlineData(@"\\.\PhysicalDrive0")]       // a device, not a file
    [InlineData(@"..\Windows")]               // relative: its starting point is not anything agreed to
    [InlineData(@"C:\Windows\CON")]           // a reserved device name
    public async Task A_location_off_this_machine_is_refused(string path)
    {
        var service = await AllowedServiceAsync();
        var reply = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest, new DirListRequest(2, path, 0).ToBytes())).Payload);

        Assert.Equal(FileStatus.NotAllowed, reply.Status);
        Assert.Empty(reply.Entries);
    }

    [Fact]
    public async Task A_folder_that_is_not_there_says_so_instead_of_failing_silently()
    {
        var service = await AllowedServiceAsync();
        var reply = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest,
                new DirListRequest(2, Path.Combine(_folder, "gone"), 0).ToBytes())).Payload);

        Assert.Equal(FileStatus.NotFound, reply.Status);
        Assert.NotEmpty(reply.Message);
    }

    // ------------------------------------------------------------------ copying a file off

    [Fact]
    public async Task A_file_arrives_byte_for_byte_and_is_written_to_the_session_log()
    {
        // Big enough to need many chunks at any sensible chunk size, and random so a reassembly
        // that silently repeats or drops a chunk cannot pass.
        var original = new byte[1_500_000];
        Random.Shared.NextBytes(original);
        string path = Path.Combine(_folder, "Invoice March.pdf");
        File.WriteAllBytes(path, original);

        var service = await AllowedServiceAsync();
        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(2, path, 0).ToBytes(), CancellationToken.None));

        var (received, end) = await CollectTransferAsync(2);

        Assert.Equal(FileStatus.Ok, end.Status);
        Assert.Equal(original.Length, end.TotalBytes);
        Assert.Equal(original, received);

        // The client must be able to read afterwards WHAT left their machine, not just that
        // something did. See SessionLog - the "no file names" promise was retired for this.
        var line = Assert.Single(_logged);
        Assert.Equal("418205793", line.Caller);
        Assert.Equal("Invoice March.pdf", line.Name);
        Assert.Equal(original.Length, line.Bytes);
        Assert.Equal(_folder, line.Folder);
    }

    [Fact]
    public async Task Every_chunk_stays_inside_the_protocol_bound()
    {
        File.WriteAllBytes(Path.Combine(_folder, "big.bin"), new byte[900_000]);

        var service = await AllowedServiceAsync();
        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(2, Path.Combine(_folder, "big.bin"), 0).ToBytes(), CancellationToken.None));

        int chunks = 0;
        while (true)
        {
            var message = await NextAsync();
            if (message.Type == MessageType.FileGetEnd) break;

            var chunk = FileChunk.FromBytes(message.Payload);
            Assert.InRange(chunk.Bytes.Length, 1, ProtocolConstants.MaxFileChunkBytes);
            chunks++;
        }

        // More than one, or the chunking was never exercised at all.
        Assert.True(chunks > 1, $"only {chunks} chunk(s) - the file did not need chunking.");
    }

    [Fact]
    public async Task Cancel_stops_a_transfer_in_flight_and_still_ends_with_a_reason()
    {
        // 40 MB, so the transfer is certainly still running when Cancel arrives.
        string path = Path.Combine(_folder, "huge.bin");
        File.WriteAllBytes(path, new byte[40 * 1024 * 1024]);

        var service = await AllowedServiceAsync();
        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(2, path, 0).ToBytes(), CancellationToken.None));

        // Read one chunk first, so the transfer is provably under way rather than not yet started.
        var first = await NextAsync();
        Assert.Equal(MessageType.FileChunk, first.Type);

        Assert.True(service.TryHandle(MessageType.FileGetCancel,
            new FileGetCancel(2).ToBytes(), CancellationToken.None));

        long delivered = FileChunk.FromBytes(first.Payload).Bytes.Length;
        FileGetEnd end;
        while (true)
        {
            var message = await NextAsync();
            if (message.Type == MessageType.FileGetEnd) { end = FileGetEnd.FromBytes(message.Payload); break; }
            delivered += FileChunk.FromBytes(message.Payload).Bytes.Length;
        }

        // A transfer that simply stops is indistinguishable from a stalled one, so it always ends
        // with a message - and it stopped early rather than quietly finishing the file.
        Assert.Equal(FileStatus.Cancelled, end.Status);
        Assert.NotEmpty(end.Message);
        Assert.True(delivered < 40 * 1024 * 1024, "the whole file was sent despite Cancel.");
        Assert.Empty(_logged); // a cancelled transfer is not logged as a file having left
    }

    [Fact]
    public async Task A_file_that_is_not_there_ends_with_a_reason_not_with_silence()
    {
        var service = await AllowedServiceAsync();
        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(2, Path.Combine(_folder, "never-existed.txt"), 0).ToBytes(), CancellationToken.None));

        var end = FileGetEnd.FromBytes((await NextAsync()).Payload);
        Assert.Equal(FileStatus.NotFound, end.Status);
        Assert.NotEmpty(end.Message);
        Assert.Empty(_logged);
    }

    [Fact]
    public async Task A_file_may_not_be_copied_before_consent()
    {
        File.WriteAllBytes(Path.Combine(_folder, "private.txt"), new byte[10]);
        var service = NewService(consent: true); // never asked

        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(2, Path.Combine(_folder, "private.txt"), 0).ToBytes(), CancellationToken.None));

        var end = FileGetEnd.FromBytes((await NextAsync()).Payload);
        Assert.Equal(FileStatus.NotAllowed, end.Status);
    }

    [Fact]
    public async Task A_second_transfer_while_one_is_running_is_refused_rather_than_started()
    {
        // Bounds how much disk work one connection can set going. A viewer that is buggy or hostile
        // must not be able to start an unbounded number of reads on somebody's machine.
        string path = Path.Combine(_folder, "huge.bin");
        File.WriteAllBytes(path, new byte[40 * 1024 * 1024]);

        var service = await AllowedServiceAsync();
        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(2, path, 0).ToBytes(), CancellationToken.None));
        Assert.Equal(MessageType.FileChunk, (await NextAsync()).Type); // the first one is under way

        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(99, path, 0).ToBytes(), CancellationToken.None));

        // The refusal is for request 99 and arrives among request 2's chunks.
        FileGetEnd busy;
        while (true)
        {
            var message = await NextAsync();
            if (message.Type != MessageType.FileGetEnd) continue;
            var end = FileGetEnd.FromBytes(message.Payload);
            if (end.RequestId == 99) { busy = end; break; }
        }

        Assert.Equal(FileStatus.Busy, busy.Status);

        // Stop the first one so the test does not sit here sending 40 MB.
        service.TryHandle(MessageType.FileGetCancel, new FileGetCancel(2).ToBytes(), CancellationToken.None);
    }

    [Fact]
    public async Task A_malformed_request_is_dropped_without_taking_the_session_down()
    {
        var service = await AllowedServiceAsync();

        // Truncated payloads: every one of these makes a length read run off the end of the message.
        Assert.True(service.TryHandle(MessageType.DirListRequest, new byte[] { 1, 2 }, CancellationToken.None));
        Assert.True(service.TryHandle(MessageType.FileGetRequest, Array.Empty<byte>(), CancellationToken.None));
        Assert.True(service.TryHandle(MessageType.FileGetCancel, new byte[] { 9 }, CancellationToken.None));

        // The channel is still usable afterwards, which is the whole claim.
        var reply = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest, new DirListRequest(7, _folder, 0).ToBytes())).Payload);
        Assert.Equal(FileStatus.Ok, reply.Status);
    }

    [Fact]
    public void A_message_that_is_not_a_file_message_is_left_for_the_rest_of_the_loop()
    {
        var service = NewService(consent: true);
        Assert.False(service.TryHandle(MessageType.Ping, Array.Empty<byte>(), CancellationToken.None));
        Assert.False(service.TryHandle(MessageType.Input, Array.Empty<byte>(), CancellationToken.None));
    }

    private async Task<(byte[] Bytes, FileGetEnd End)> CollectTransferAsync(int requestId)
    {
        using var buffer = new MemoryStream();
        while (true)
        {
            var message = await NextAsync();
            if (message.Type == MessageType.FileGetEnd)
                return (buffer.ToArray(), FileGetEnd.FromBytes(message.Payload));

            var chunk = FileChunk.FromBytes(message.Payload);
            Assert.Equal(requestId, chunk.RequestId);
            // The offset must be exactly where the last chunk left off, or the file is reassembled
            // wrong on the other side with no error anywhere.
            Assert.Equal(buffer.Length, chunk.Offset);
            buffer.Write(chunk.Bytes);
        }
    }
}
