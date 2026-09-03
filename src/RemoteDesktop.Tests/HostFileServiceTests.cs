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

    /// <summary>The same, for files that ARRIVED — with the two facts the log distinguishes.</summary>
    private readonly List<(string Name, long Bytes, string Folder, bool IsProgram, bool Replaced)> _arrived = new();

    /// <summary>What the incoming-file question was asked about, and what it was told to answer.</summary>
    private readonly List<(string Name, long Bytes, string Folder, bool IsProgram)> _asked = new();

    private bool _allowIncoming = true;
    private ReplaceChoice _replaceAnswer = ReplaceChoice.Refuse;
    private string _configFolder = string.Empty;

    private HostFileService NewService(bool consent, bool withUpload = true, string? sessionLogPath = null)
    {
        _configFolder = Path.Combine(_folder, "..", "config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configFolder);

        return new HostFileService(
            _hostChannel!,
            new BandwidthGovernor(),
            "418205793",
            new PartialFiles(_configFolder),
            _ => Task.FromResult(consent),
            (caller, name, bytes, folder) => { lock (_logged) _logged.Add((caller, name, bytes, folder)); },
            withUpload ? (_, name, bytes, folder, isProgram) =>
            {
                lock (_asked) _asked.Add((name, bytes, folder, isProgram));
                return Task.FromResult(_allowIncoming);
            }
            : null,
            withUpload ? (_, _, _) => Task.FromResult(_replaceAnswer) : null,
            withUpload ? (_, name, bytes, folder, isProgram, replaced) =>
            {
                lock (_arrived) _arrived.Add((name, bytes, folder, isProgram, replaced));
            }
            : null,
            sessionLogPath: sessionLogPath);
    }

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
            new PartialFiles(_folder),
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
        var service = new HostFileService(_hostChannel!, new BandwidthGovernor(), "418205793",
            new PartialFiles(_folder), null, null);

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
    public async Task The_reserved_session_log_request_serves_the_hosts_own_log_not_a_typed_path()
    {
        // Deliberately OUTSIDE _folder (the normal, operator-browsable area) and never resolved
        // through RemotePath/LocalDrives — this is the whole point: the operator cannot reach this
        // path by typing, only by the reserved request, and the host answers it from its own known
        // location regardless of what the operator's own listing folder is.
        // Path.GetFullPath, not a bare Path.Combine with "..": OpenedPath's handle re-check compares
        // this string against the file handle's own REAL, normalised path, and an un-normalised ".."
        // segment fails that comparison even though it points at the identical file on disk.
        string logFolder = Path.GetFullPath(Path.Combine(_folder, "..", "appdata-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(logFolder);
        string logPath = Path.Combine(logFolder, "sessions.txt");
        byte[] original = System.Text.Encoding.UTF8.GetBytes("CONNECTED  418 205 793 accepted and connected\r\n");
        File.WriteAllBytes(logPath, original);

        var service = NewService(consent: true, sessionLogPath: logPath);
        await ExchangeAsync(service, MessageType.FileAccessRequest, new FileAccessRequest(1).ToBytes());

        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(2, FileGetRequest.SessionLogPath, 0).ToBytes(), CancellationToken.None));

        var (received, end) = await CollectTransferAsync(2);

        Assert.Equal(FileStatus.Ok, end.Status);
        Assert.Equal(original, received);
    }

    [Fact]
    public async Task The_reserved_session_log_request_fails_plainly_when_there_is_no_log_yet()
    {
        // No sessionLogPath configured at all — the state on a brand-new install before any
        // session has ever completed.
        var service = NewService(consent: true, sessionLogPath: null);
        await ExchangeAsync(service, MessageType.FileAccessRequest, new FileAccessRequest(1).ToBytes());

        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(2, FileGetRequest.SessionLogPath, 0).ToBytes(), CancellationToken.None));

        var msg = await NextAsync();
        Assert.Equal(MessageType.FileGetEnd, msg.Type);
        var end = FileGetEnd.FromBytes(msg.Payload);
        Assert.Equal(FileStatus.NotFound, end.Status);
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

    // ------------------------------------------------------------------ putting a file ON the machine

    /// <summary>Drives a whole upload the way a viewer would, and returns how the host said it went.</summary>
    private async Task<(FileSendReply Reply, FileSendResult? Result)> UploadAsync(
        HostFileService service, int id, string name, byte[] content, string? folder = null)
    {
        Assert.True(service.TryHandle(MessageType.FileSendRequest,
            new FileSendRequest(id, folder ?? _folder, name, content.Length).ToBytes(), CancellationToken.None));

        var reply = FileSendReply.FromBytes((await NextAsync()).Payload);
        if (reply.Status != FileStatus.Ok) return (reply, null);

        for (int offset = 0; offset < content.Length; offset += 64 * 1024)
        {
            int size = Math.Min(64 * 1024, content.Length - offset);
            Assert.True(service.TryHandle(MessageType.FileSendChunk,
                new FileChunk(id, offset, content.AsSpan(offset, size).ToArray()).ToBytes(), CancellationToken.None));
        }

        return (reply, FileSendResult.FromBytes((await NextAsync()).Payload));
    }

    [Fact]
    public async Task A_file_lands_byte_for_byte_and_the_partial_is_gone()
    {
        var content = new byte[700_000];
        Random.Shared.NextBytes(content);

        var service = NewService(consent: true);
        var (reply, result) = await UploadAsync(service, 5, "driver-setup.zip", content);

        Assert.Equal(FileStatus.Ok, reply.Status);
        Assert.Equal(FileStatus.Ok, result!.Value.Status);
        Assert.Equal(content.Length, result.Value.WrittenBytes);

        string landed = Path.Combine(_folder, "driver-setup.zip");
        Assert.Equal(content, File.ReadAllBytes(landed));

        // Nothing left behind wearing a temporary name, and nothing left in the ledger.
        Assert.Single(Directory.GetFiles(_folder));
        Assert.False(File.Exists(Path.Combine(_configFolder, PartialFiles.LedgerFileName)));

        var line = Assert.Single(_arrived);
        Assert.Equal("driver-setup.zip", line.Name);
        Assert.Equal(content.Length, line.Bytes);
        Assert.Equal(_folder, line.Folder);
        Assert.False(line.IsProgram);
        Assert.False(line.Replaced);
    }

    [Fact]
    public async Task Refusing_the_file_means_nothing_is_written_at_all()
    {
        _allowIncoming = false;
        var service = NewService(consent: true);

        var (reply, result) = await UploadAsync(service, 5, "invoice.pdf", new byte[1000]);

        Assert.Equal(FileStatus.RefusedByPerson, reply.Status);
        Assert.Null(result);
        Assert.Empty(Directory.GetFiles(_folder)); // not even a partial
        Assert.Empty(_arrived);
    }

    [Fact]
    public async Task A_missing_question_refuses_rather_than_writes()
    {
        // Fail closed. A build wired up without the dialog must not quietly accept files.
        var service = NewService(consent: true, withUpload: false);
        var (reply, _) = await UploadAsync(service, 5, "invoice.pdf", new byte[10]);

        Assert.Equal(FileStatus.RefusedByPerson, reply.Status);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task A_PROGRAM_is_asked_about_by_name_every_single_time()
    {
        // The general yes is given ONCE. A program is asked about again, because the first upload
        // being a text file must not make every executable after it arrive in silence.
        var service = NewService(consent: true);

        await UploadAsync(service, 5, "notes.txt", new byte[10]);
        await UploadAsync(service, 6, "setup.exe", new byte[10]);
        await UploadAsync(service, 7, "other.txt", new byte[10]);
        await UploadAsync(service, 8, "tool.bat", new byte[10]);

        // notes.txt (the once-per-connection question), then setup.exe, then tool.bat.
        // other.txt is NOT asked about again.
        Assert.Equal(3, _asked.Count);
        Assert.Equal("notes.txt", _asked[0].Name);
        Assert.Equal("setup.exe", _asked[1].Name);
        Assert.True(_asked[1].IsProgram);
        Assert.Equal("tool.bat", _asked[2].Name);
        Assert.True(_asked[2].IsProgram);

        // And the log can tell a person that software arrived, without them knowing what .exe means.
        Assert.Equal(2, _arrived.Count(a => a.IsProgram));
    }

    [Fact]
    public async Task The_question_names_the_file_its_size_and_the_FULL_folder()
    {
        var service = NewService(consent: true);
        await UploadAsync(service, 5, "Report.docx", new byte[2048]);

        var asked = Assert.Single(_asked);
        Assert.Equal("Report.docx", asked.Name);
        Assert.Equal(2048, asked.Bytes);
        // The full path, not "Documents": a folder name alone is ambiguous across profiles.
        Assert.Equal(_folder, asked.Folder);
        Assert.True(Path.IsPathFullyQualified(asked.Folder));
    }

    [Theory]
    [InlineData(@"sub\evil.txt")]        // a separator would move the write out of the folder
    [InlineData("../evil.txt")]
    [InlineData("evil.exe.")]            // Windows strips the trailing dot AFTER any check
    [InlineData("evil.exe ")]
    [InlineData("CON")]
    [InlineData("report.txt:hidden")]    // an alternate data stream Explorer does not show
    [InlineData("")]
    public async Task A_name_that_is_not_a_bare_name_is_refused(string name)
    {
        var service = NewService(consent: true);
        Assert.True(service.TryHandle(MessageType.FileSendRequest,
            new FileSendRequest(5, _folder, name, 10).ToBytes(), CancellationToken.None));

        var reply = FileSendReply.FromBytes((await NextAsync()).Payload);
        Assert.Equal(FileStatus.NotAllowed, reply.Status);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Empty(_asked); // refused before the person was even troubled
    }

    [Theory]
    [InlineData(@"\\fileserver\finance")]
    [InlineData(@"\\?\C:\Windows")]
    public async Task A_destination_folder_off_this_machine_is_refused(string folder)
    {
        var service = NewService(consent: true);
        Assert.True(service.TryHandle(MessageType.FileSendRequest,
            new FileSendRequest(5, folder, "note.txt", 10).ToBytes(), CancellationToken.None));

        Assert.Equal(FileStatus.NotAllowed, FileSendReply.FromBytes((await NextAsync()).Payload).Status);
    }

    [Fact]
    public async Task Keep_both_saves_the_new_file_beside_the_old_one_and_says_the_new_name()
    {
        string existing = Path.Combine(_folder, "Report.docx");
        File.WriteAllText(existing, "the one they already had");

        _replaceAnswer = ReplaceChoice.KeepBoth;
        var service = NewService(consent: true);
        var (reply, result) = await UploadAsync(service, 5, "Report.docx", new byte[64]);

        Assert.Equal(FileStatus.Ok, reply.Status);
        // The operator is TOLD the name, or they would assume theirs replaced the other.
        Assert.Equal("Report (2).docx", reply.SavedAs);
        Assert.Equal(FileStatus.Ok, result!.Value.Status);

        Assert.Equal("the one they already had", File.ReadAllText(existing));
        Assert.Equal(64, new FileInfo(Path.Combine(_folder, "Report (2).docx")).Length);
        Assert.False(_arrived.Single().Replaced);
    }

    [Fact]
    public async Task Replace_overwrites_only_after_they_said_so_and_is_logged_as_a_replacement()
    {
        string existing = Path.Combine(_folder, "config.ini");
        File.WriteAllText(existing, "old");

        _replaceAnswer = ReplaceChoice.Replace;
        var service = NewService(consent: true);
        var (_, result) = await UploadAsync(service, 5, "config.ini", new byte[] { 1, 2, 3, 4 });

        Assert.Equal(FileStatus.Ok, result!.Value.Status);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(existing));
        Assert.Single(Directory.GetFiles(_folder));
        Assert.True(_arrived.Single().Replaced);
    }

    [Fact]
    public async Task Refusing_the_replacement_keeps_their_file_untouched()
    {
        string existing = Path.Combine(_folder, "config.ini");
        File.WriteAllText(existing, "mine");

        _replaceAnswer = ReplaceChoice.Refuse;
        var service = NewService(consent: true);
        var (reply, _) = await UploadAsync(service, 5, "config.ini", new byte[] { 9 });

        Assert.Equal(FileStatus.RefusedByPerson, reply.Status);
        Assert.Equal("mine", File.ReadAllText(existing));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task More_bytes_than_promised_are_refused_and_nothing_is_kept()
    {
        var service = NewService(consent: true);

        // Declares 100 bytes and then pushes 200. The declared size is what the free-space check and
        // the question shown to the person were based on, so exceeding it is not a small matter.
        Assert.True(service.TryHandle(MessageType.FileSendRequest,
            new FileSendRequest(5, _folder, "lying.bin", 100).ToBytes(), CancellationToken.None));
        Assert.Equal(FileStatus.Ok, FileSendReply.FromBytes((await NextAsync()).Payload).Status);

        Assert.True(service.TryHandle(MessageType.FileSendChunk,
            new FileChunk(5, 0, new byte[200]).ToBytes(), CancellationToken.None));

        var result = FileSendResult.FromBytes((await NextAsync()).Payload);
        Assert.Equal(FileStatus.WriteError, result.Status);
        Assert.Empty(Directory.GetFiles(_folder)); // the partial is removed, not left as litter
        Assert.Empty(_arrived);
    }

    [Fact]
    public async Task A_chunk_at_the_wrong_place_ends_the_upload_instead_of_scattering_bytes()
    {
        var service = NewService(consent: true);

        Assert.True(service.TryHandle(MessageType.FileSendRequest,
            new FileSendRequest(5, _folder, "jumpy.bin", 1000).ToBytes(), CancellationToken.None));
        Assert.Equal(FileStatus.Ok, FileSendReply.FromBytes((await NextAsync()).Payload).Status);

        // Offset 500 with nothing before it. Only the next bytes in sequence are ever accepted.
        Assert.True(service.TryHandle(MessageType.FileSendChunk,
            new FileChunk(5, 500, new byte[100]).ToBytes(), CancellationToken.None));

        var result = FileSendResult.FromBytes((await NextAsync()).Payload);
        Assert.NotEqual(FileStatus.Ok, result.Status);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Cancelling_an_upload_leaves_no_file_and_no_ledger_entry()
    {
        var service = NewService(consent: true);

        Assert.True(service.TryHandle(MessageType.FileSendRequest,
            new FileSendRequest(5, _folder, "big.bin", 5_000_000).ToBytes(), CancellationToken.None));
        Assert.Equal(FileStatus.Ok, FileSendReply.FromBytes((await NextAsync()).Payload).Status);

        Assert.True(service.TryHandle(MessageType.FileSendChunk,
            new FileChunk(5, 0, new byte[64_000]).ToBytes(), CancellationToken.None));
        Assert.True(service.TryHandle(MessageType.FileGetCancel,
            new FileGetCancel(5).ToBytes(), CancellationToken.None));

        var result = FileSendResult.FromBytes((await NextAsync()).Payload);
        Assert.Equal(FileStatus.Cancelled, result.Status);
        Assert.NotEmpty(result.Message);

        // The whole point of PartialFiles: nothing half-written is left on a stranger's disk, and
        // nothing is left in the ledger for a start-up sweep to find either.
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.False(File.Exists(Path.Combine(_configFolder, PartialFiles.LedgerFileName)));
    }

    [Fact]
    public async Task A_file_bigger_than_the_disk_is_refused_before_a_byte_is_written()
    {
        var service = NewService(consent: true);
        Assert.True(service.TryHandle(MessageType.FileSendRequest,
            new FileSendRequest(5, _folder, "enormous.bin", long.MaxValue / 2).ToBytes(), CancellationToken.None));

        var reply = FileSendReply.FromBytes((await NextAsync()).Payload);
        Assert.Equal(FileStatus.NoRoom, reply.Status);
        Assert.NotEmpty(reply.Message);
        Assert.Empty(Directory.GetFiles(_folder));

        // The person IS asked first, and that is deliberate — it changed on 2026-08-06 and this row
        // changed with it. Checking the disk before the answer looks tidier and leaks: it lets a
        // refused operator tell "folder exists" from "folder does not" for any path they like, with
        // nothing appearing on the client's screen, which enumerates their account names and their
        // installed software. Nothing may be learned from the disk before consent.
        Assert.Single(_asked);
    }

    [Fact]
    public async Task After_a_refusal_the_disk_answers_no_more_questions()
    {
        // The oracle in full: refuse once, then probe. Every answer must be identical whether the
        // folder exists or not, or the refusal was only about writing and not about knowing.
        _allowIncoming = false;
        var service = NewService(consent: true);

        var first = await UploadAsync(service, 5, "a.txt", new byte[10]);
        Assert.Equal(FileStatus.RefusedByPerson, first.Reply.Status);

        var real = await UploadAsync(service, 6, "b.txt", new byte[10], _folder);
        var imaginary = await UploadAsync(service, 7, "b.txt", new byte[10], Path.Combine(_folder, "no-such-folder"));

        Assert.Equal(FileStatus.RefusedByPerson, real.Reply.Status);
        Assert.Equal(FileStatus.RefusedByPerson, imaginary.Reply.Status);
        Assert.Equal(real.Reply.Message, imaginary.Reply.Message);

        // And a size no disk could hold is answered the same way, so free space cannot be bisected.
        Assert.True(service.TryHandle(MessageType.FileSendRequest,
            new FileSendRequest(8, _folder, "huge.bin", long.MaxValue / 2).ToBytes(), CancellationToken.None));
        Assert.Equal(FileStatus.RefusedByPerson, FileSendReply.FromBytes((await NextAsync()).Payload).Status);

        Assert.Single(_asked); // asked exactly once, at the start, and never again
    }

    [Fact]
    public async Task An_empty_file_still_lands()
    {
        // Zero bytes is a real file and the loop must not wait forever for a chunk that never comes.
        var service = NewService(consent: true);
        var (reply, result) = await UploadAsync(service, 5, "empty.txt", Array.Empty<byte>());

        Assert.Equal(FileStatus.Ok, reply.Status);
        Assert.Equal(FileStatus.Ok, result!.Value.Status);
        Assert.True(File.Exists(Path.Combine(_folder, "empty.txt")));
    }

    [Fact]
    public async Task Chunks_arriving_with_no_upload_agreed_are_dropped()
    {
        var service = NewService(consent: true);

        // No FileSendRequest at all. The bytes must go nowhere, not into a file of their choosing.
        Assert.True(service.TryHandle(MessageType.FileSendChunk,
            new FileChunk(5, 0, new byte[100]).ToBytes(), CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_folder));

        // And the channel still works afterwards.
        var reply = DirListReply.FromBytes(
            (await ExchangeAsync(await AllowedServiceAsync(), MessageType.DirListRequest,
                new DirListRequest(9, _folder, 0).ToBytes())).Payload);
        Assert.Equal(FileStatus.Ok, reply.Status);
    }

    // ------------------------------------------------------------------ found by an adversarial read

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetShortPathNameW(string path, System.Text.StringBuilder shortPath, int length);

    [Fact]
    public async Task A_short_name_alias_cannot_be_used_to_destroy_a_file_the_dialog_never_names()
    {
        // THE ATTACK: Windows keeps an 8.3 alias for a long file name, and Path.GetFullPath expands
        // it. So "IMPORT~1.DOC" resolves onto "important-document.docx". Every path rule passes, the
        // overwrite dialog says "They are sending IMPORT~1.DOC" — a name the person has never seen —
        // they conclude it is junk, press Replace, and their own document is destroyed under a name
        // that appeared nowhere on their screen. The session log records the same fiction.
        string real = Path.Combine(_folder, "important-document.docx");
        File.WriteAllText(real, "somebody's actual work");

        var buffer = new System.Text.StringBuilder(300);
        GetShortPathNameW(real, buffer, buffer.Capacity);
        string alias = Path.GetFileName(buffer.ToString());

        // 8.3 alias creation can be switched off per volume. If it is, there is nothing to attack
        // here and saying so is better than a test that silently proves nothing.
        if (string.Equals(alias, "important-document.docx", StringComparison.OrdinalIgnoreCase) || alias.Length == 0)
            return;

        _replaceAnswer = ReplaceChoice.Replace; // the person is talked into it
        var service = NewService(consent: true);

        Assert.True(service.TryHandle(MessageType.FileSendRequest,
            new FileSendRequest(5, _folder, alias, 16).ToBytes(), CancellationToken.None));
        var reply = FileSendReply.FromBytes((await NextAsync()).Payload);

        Assert.Equal(FileStatus.NotAllowed, reply.Status);
        Assert.Equal("somebody's actual work", File.ReadAllText(real));
        Assert.Empty(_asked);    // and the person was never troubled with the misleading name
        Assert.Empty(_arrived);
    }

    [Fact]
    public async Task A_yes_for_one_folder_is_not_a_yes_for_every_folder()
    {
        // The dialog says "into <folder>" and then "Nothing else on this computer is changed".
        // Until this was fixed, one yes for a readme in Downloads licensed writes anywhere the
        // account could reach for the rest of the connection.
        string other = Path.Combine(_folder, "elsewhere");
        Directory.CreateDirectory(other);

        var service = NewService(consent: true);

        await UploadAsync(service, 5, "readme.txt", new byte[8]);
        Assert.Single(_asked);
        Assert.Equal(_folder, _asked[0].Folder);

        await UploadAsync(service, 6, "second.txt", new byte[8]);
        Assert.Single(_asked); // same folder: not asked again, as intended

        await UploadAsync(service, 7, "user.js", new byte[8], other);
        Assert.Equal(2, _asked.Count);         // a DIFFERENT folder is a different question
        Assert.Equal(other, _asked[1].Folder);
    }

    [Fact]
    public async Task A_folder_that_points_somewhere_else_is_not_listed()
    {
        // A directory link is invisible in a string: C:\Projects can BE \\fileserver\finance, and
        // every check that reads the path agrees it is on drive C. Only the open handle knows.
        // A UNC target needs a privilege this account does not have, so the link here is local —
        // which still proves the handle check RUNS on the listing path, which is what was missing.
        string real = Path.Combine(_folder, "real");
        string link = Path.Combine(_folder, "link");
        Directory.CreateDirectory(real);
        File.WriteAllBytes(Path.Combine(real, "secret.txt"), new byte[10]);

        var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", $"/c mklink /J \"{link}\" \"{real}\"") { UseShellExecute = false, CreateNoWindow = true });
        made!.WaitForExit();
        if (!Directory.Exists(link)) return; // no junction, nothing to prove

        var service = await AllowedServiceAsync();
        var reply = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest, new DirListRequest(9, link, 0).ToBytes())).Payload);

        Assert.Equal(FileStatus.NotAllowed, reply.Status);
        Assert.Empty(reply.Entries);

        // ...and the real folder still lists perfectly, so the check refuses the redirection and not
        // ordinary work.
        var honest = DirListReply.FromBytes(
            (await ExchangeAsync(service, MessageType.DirListRequest, new DirListRequest(10, real, 0).ToBytes())).Payload);
        Assert.Equal(FileStatus.Ok, honest.Status);
        Assert.Equal("secret.txt", Assert.Single(honest.Entries).Name);

        // Remove the junction itself, or the folder cleanup cannot delete the tree and the test
        // leaves litter behind — which would be a poor advertisement for this feature.
        try { Directory.Delete(link); } catch { }
    }

    [Fact]
    public async Task Hidden_data_inside_a_file_cannot_be_read()
    {
        // An alternate data stream is a second body of bytes attached to a file that appears in no
        // listing at all — not ours, not Explorer's. Reading one would hand over content the person
        // has no way of knowing is there.
        string host = Path.Combine(_folder, "innocent.txt");
        File.WriteAllText(host, "nothing to see");
        try { File.WriteAllText(host + ":hidden", "the real payload"); }
        catch { return; } // the volume does not support streams

        var service = await AllowedServiceAsync();
        Assert.True(service.TryHandle(MessageType.FileGetRequest,
            new FileGetRequest(11, host + ":hidden", 0).ToBytes(), CancellationToken.None));

        var end = FileGetEnd.FromBytes((await NextAsync()).Payload);
        Assert.Equal(FileStatus.NotAllowed, end.Status);
        Assert.Equal(0, end.TotalBytes);
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
