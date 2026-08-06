using System.Net;
using System.Net.Sockets;
using RemoteDesktop.Host.Files;
using RemoteDesktop.Host.Net;
using RemoteDesktop.Shared.Files;
using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.Viewer.Files;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// BOTH HALVES, TALKING TO EACH OTHER. The operator's <see cref="ViewerFileClient"/> drives the
/// client's <see cref="HostFileService"/> over a real socket, with real files on a real disk, and
/// both sides running their own message pump exactly as they do in the product.
///
/// <para>This is the closest thing to the two-machine test that can exist before the panel does, and
/// it is worth more than either side's own tests: almost every way this feature can be wrong is a
/// disagreement between the two halves — a request number that is not echoed, an offset counted from
/// the wrong place, a reply nobody is waiting for, a name the other side never hears about. Neither
/// side's tests can see any of that on its own.</para>
///
/// <para><b>What it still is NOT:</b> two machines, two home connections, or a human pressing
/// anything. The consent answers here are supplied by the test, and every dialog the client would
/// actually see is bypassed. That gap closes at the first two-person test, not here.</para>
/// </summary>
public class FileTransferEndToEndTests : IAsyncLifetime
{
    private string _clientFolder = string.Empty;   // "their" machine
    private string _operatorFolder = string.Empty; // "my" machine
    private string _clientConfig = string.Empty;
    private string _operatorConfig = string.Empty;
    private string _root = string.Empty;

    private TcpListener? _listener;
    private TcpClient? _hostSide;
    private TcpClient? _viewerSide;
    private MessageChannel? _hostChannel;
    private MessageChannel? _viewerChannel;
    private HostFileService? _service;
    private ViewerFileClient? _client;
    private CancellationTokenSource? _pumps;

    private bool _allowAccess = true;
    private bool _allowIncoming = true;
    private ReplaceChoice _replaceAnswer = ReplaceChoice.Refuse;
    private readonly List<string> _arrived = new();

    public async Task InitializeAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "flashdesk-e2e-" + Guid.NewGuid().ToString("N"));
        _clientFolder = Path.Combine(_root, "their-machine", "Documents");
        _operatorFolder = Path.Combine(_root, "my-machine", "Downloads");
        _clientConfig = Path.Combine(_root, "their-machine", "config");
        _operatorConfig = Path.Combine(_root, "my-machine", "config");
        foreach (string d in new[] { _clientFolder, _operatorFolder, _clientConfig, _operatorConfig })
            Directory.CreateDirectory(d);

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var accepting = _listener.AcceptTcpClientAsync();
        _viewerSide = new TcpClient();
        await _viewerSide.ConnectAsync((IPEndPoint)_listener.LocalEndpoint);
        _hostSide = await accepting;

        _hostChannel = new MessageChannel(_hostSide.GetStream());
        _viewerChannel = new MessageChannel(_viewerSide.GetStream());

        _service = new HostFileService(
            _hostChannel, new BandwidthGovernor(), "418205793", new PartialFiles(_clientConfig),
            _ => Task.FromResult(_allowAccess),
            null,
            (_, _, _, _, _) => Task.FromResult(_allowIncoming),
            (_, _, _) => Task.FromResult(_replaceAnswer),
            (_, name, _, _, _, _) => { lock (_arrived) _arrived.Add(name); });

        _client = new ViewerFileClient(_viewerChannel, new PartialFiles(_operatorConfig));

        // Both message pumps, doing exactly what the real loops do: read one message, hand it over,
        // read the next. Nothing in either pump touches a disk.
        _pumps = new CancellationTokenSource();
        _ = Pump(_hostChannel, (t, p) => _service.TryHandle(t, p, _pumps.Token), _pumps.Token);
        _ = Pump(_viewerChannel, (t, p) => _client.TryHandle(t, p), _pumps.Token);
    }

    private static Task Pump(MessageChannel channel, Func<MessageType, byte[], bool> handle, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var message = await channel.ReceiveAsync(ct);
                    if (message is null) break;
                    handle(message.Value.Type, message.Value.Payload);
                }
            }
            catch { /* the socket closed: the test is over */ }
        }, ct);

    public Task DisposeAsync()
    {
        _pumps?.Cancel();
        _client?.Dispose();
        _service?.Dispose();
        _hostChannel?.Dispose();
        _viewerChannel?.Dispose();
        _hostSide?.Dispose();
        _viewerSide?.Dispose();
        _listener?.Stop();
        try { Directory.Delete(_root, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    private static CancellationTokenSource Timeout() => new(TimeSpan.FromSeconds(30));

    private async Task AllowAsync()
    {
        using var ct = Timeout();
        var reply = await _client!.RequestAccessAsync(ct.Token);
        Assert.True(reply.Granted);
        Assert.True(_client.Allowed);
    }

    // ------------------------------------------------------------------ the whole journey

    [Fact]
    public async Task Ask_list_and_copy_a_file_across_exactly_as_the_panel_will()
    {
        var original = new byte[1_200_000];
        Random.Shared.NextBytes(original);
        File.WriteAllBytes(Path.Combine(_clientFolder, "Invoice March.pdf"), original);
        Directory.CreateDirectory(Path.Combine(_clientFolder, "Old invoices"));

        await AllowAsync();
        using var ct = Timeout();

        var listing = await _client!.ListAsync(_clientFolder, 0, ct.Token);
        Assert.Equal(FileStatus.Ok, listing.Status);
        Assert.True(listing.Entries[0].IsDirectory);                         // folders first, always
        Assert.Contains(listing.Entries, e => e.Name == "Invoice March.pdf");

        long lastProgress = 0;
        var end = await _client.DownloadAsync(
            Path.Combine(_clientFolder, "Invoice March.pdf"), _operatorFolder, "Invoice March.pdf",
            new Progress<long>(n => lastProgress = n), ct.Token);

        Assert.Equal(FileStatus.Ok, end.Status);
        Assert.Equal(original.Length, end.TotalBytes);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(_operatorFolder, "Invoice March.pdf")));

        // Nothing left wearing a temporary name on the operator's own disk either.
        Assert.Single(Directory.GetFiles(_operatorFolder));
        Assert.False(File.Exists(Path.Combine(_operatorConfig, PartialFiles.LedgerFileName)));
        Assert.True(lastProgress > 0, "the panel would have had no progress to show.");
    }

    [Fact]
    public async Task Refusing_access_stops_everything_that_follows()
    {
        File.WriteAllBytes(Path.Combine(_clientFolder, "private.txt"), new byte[100]);
        _allowAccess = false;

        using var ct = Timeout();
        var reply = await _client!.RequestAccessAsync(ct.Token);

        Assert.False(reply.Granted);
        Assert.False(_client.Allowed);
        Assert.NotEmpty(reply.Reason); // the operator is told, not left guessing

        var listing = await _client.ListAsync(_clientFolder, 0, ct.Token);
        Assert.Equal(FileStatus.NotAllowed, listing.Status);

        var end = await _client.DownloadAsync(
            Path.Combine(_clientFolder, "private.txt"), _operatorFolder, "private.txt", null, ct.Token);
        Assert.Equal(FileStatus.NotAllowed, end.Status);
        Assert.Empty(Directory.GetFiles(_operatorFolder));
    }

    [Fact]
    public async Task A_folder_of_many_files_pages_through_with_nothing_lost()
    {
        const int total = DirListReply.PageSize + 17;
        for (int i = 0; i < total; i++)
            File.WriteAllBytes(Path.Combine(_clientFolder, $"log-{i:0000}.txt"), Array.Empty<byte>());

        await AllowAsync();
        using var ct = Timeout();

        var names = new List<string>();
        int skip = 0;
        while (true)
        {
            var page = await _client!.ListAsync(_clientFolder, skip, ct.Token);
            Assert.Equal(FileStatus.Ok, page.Status);
            names.AddRange(page.Entries.Select(e => e.Name));
            if (!page.HasMore) break;
            skip += page.Entries.Count;
        }

        Assert.Equal(total, names.Count);
        Assert.Equal(total, names.Distinct().Count());
    }

    // ------------------------------------------------------------------ the other direction

    [Fact]
    public async Task A_file_goes_the_other_way_and_lands_under_the_name_they_agreed_to()
    {
        var content = new byte[800_000];
        Random.Shared.NextBytes(content);
        string mine = Path.Combine(_operatorFolder, "driver-setup.zip");
        File.WriteAllBytes(mine, content);

        await AllowAsync();
        using var ct = Timeout();

        long lastProgress = 0;
        var (reply, result) = await _client!.UploadAsync(mine, _clientFolder,
            new Progress<long>(n => lastProgress = n), ct.Token);

        Assert.Equal(FileStatus.Ok, reply.Status);
        Assert.Equal(FileStatus.Ok, result!.Value.Status);
        Assert.Equal(content.Length, result.Value.WrittenBytes);
        Assert.Equal(content, File.ReadAllBytes(Path.Combine(_clientFolder, "driver-setup.zip")));
        Assert.Equal("driver-setup.zip", Assert.Single(_arrived));
        Assert.True(lastProgress > 0);

        // And nothing half-written left on their machine.
        Assert.Single(Directory.GetFiles(_clientFolder));
    }

    [Fact]
    public async Task Keep_both_tells_the_operator_the_name_it_was_actually_saved_as()
    {
        File.WriteAllText(Path.Combine(_clientFolder, "Report.docx"), "theirs");
        string mine = Path.Combine(_operatorFolder, "Report.docx");
        File.WriteAllText(mine, "mine");
        _replaceAnswer = ReplaceChoice.KeepBoth;

        await AllowAsync();
        using var ct = Timeout();

        var (reply, result) = await _client!.UploadAsync(mine, _clientFolder, null, ct.Token);

        Assert.Equal(FileStatus.Ok, reply.Status);
        // Without this the operator would assume their file had replaced the other one.
        Assert.Equal("Report (2).docx", reply.SavedAs);
        Assert.Equal(FileStatus.Ok, result!.Value.Status);

        Assert.Equal("theirs", File.ReadAllText(Path.Combine(_clientFolder, "Report.docx")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_clientFolder, "Report (2).docx")));
    }

    [Fact]
    public async Task Refusing_the_incoming_file_leaves_their_folder_untouched()
    {
        string mine = Path.Combine(_operatorFolder, "unwanted.txt");
        File.WriteAllText(mine, "no thank you");
        _allowIncoming = false;

        await AllowAsync();
        using var ct = Timeout();

        var (reply, result) = await _client!.UploadAsync(mine, _clientFolder, null, ct.Token);

        Assert.Equal(FileStatus.RefusedByPerson, reply.Status);
        Assert.Null(result);
        Assert.Empty(Directory.GetFiles(_clientFolder));
        Assert.Empty(_arrived);
    }

    // ------------------------------------------------------------------ stopping half way

    [Fact]
    public async Task Cancelling_a_download_leaves_no_half_file_on_either_machine()
    {
        File.WriteAllBytes(Path.Combine(_clientFolder, "huge.bin"), new byte[60 * 1024 * 1024]);

        await AllowAsync();

        using var ct = new CancellationTokenSource();
        var started = new TaskCompletionSource();

        var download = _client!.DownloadAsync(
            Path.Combine(_clientFolder, "huge.bin"), _operatorFolder, "huge.bin",
            new Progress<long>(_ => started.TrySetResult()), ct.Token);

        // Cancel only once bytes are provably moving, or this would test the wrong thing.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        ct.Cancel();

        var end = await download;

        Assert.Equal(FileStatus.Cancelled, end.Status);
        // The operator's disk: no partial, no ledger entry, and certainly no "huge.bin".
        Assert.Empty(Directory.GetFiles(_operatorFolder));
        Assert.False(File.Exists(Path.Combine(_operatorConfig, PartialFiles.LedgerFileName)));
        // Their file is untouched - a download never changes the machine it reads from.
        Assert.True(File.Exists(Path.Combine(_clientFolder, "huge.bin")));
    }

    [Fact]
    public async Task A_reply_for_a_request_the_operator_has_moved_on_from_is_dropped()
    {
        // Two listings asked for in a row. Each answer must find its OWN request - this is what the
        // request number exists for, and getting it wrong would draw one folder's contents under
        // another folder's name.
        Directory.CreateDirectory(Path.Combine(_clientFolder, "first"));
        Directory.CreateDirectory(Path.Combine(_clientFolder, "second"));
        File.WriteAllBytes(Path.Combine(_clientFolder, "first", "a.txt"), new byte[1]);
        File.WriteAllBytes(Path.Combine(_clientFolder, "second", "b.txt"), new byte[1]);

        await AllowAsync();
        using var ct = Timeout();

        var first = _client!.ListAsync(Path.Combine(_clientFolder, "first"), 0, ct.Token);
        var second = _client.ListAsync(Path.Combine(_clientFolder, "second"), 0, ct.Token);

        var firstReply = await first;
        var secondReply = await second;

        Assert.Equal("a.txt", Assert.Single(firstReply.Entries).Name);
        Assert.Equal("b.txt", Assert.Single(secondReply.Entries).Name);
        Assert.NotEqual(firstReply.RequestId, secondReply.RequestId);
    }
}
