using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using RemoteDesktop.Shared.Files;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Viewer.Files;

/// <summary>
/// The operator's half of file transfer: asks, lists, downloads, uploads, cancels. It owns no
/// socket and no window — it is handed a <see cref="MessageChannel"/> and fed the messages that
/// arrive on it, which is what lets it be driven end to end against the real host service over a
/// real connection rather than against a mock of one.
///
/// <para><b>REQUESTS AND REPLIES ARE PAIRED BY NUMBER, and that is new to this protocol.</b>
/// Everything before it was one-way or self-correlating. Here the operator can click a folder,
/// change their mind, and click another before the first answer arrives — so a reply that belongs to
/// a folder nobody is looking at any more must be recognisable and droppable. Every request gets an
/// increasing number and the host echoes it; an answer whose number nobody is waiting for is
/// discarded rather than drawn.</para>
///
/// <para><b>Nothing here touches the receive loop's thread for longer than a dictionary lookup.</b>
/// Arriving chunks are queued; the disk write happens on the transfer's own task. The receive loop
/// also carries the video, so a slow disk on the operator's side must not stall the picture.</para>
///
/// <para><b>Both directions write to a temporary name and rename at the last byte</b> — the
/// operator's own disk deserves the same treatment as the client's, and it is the same
/// <see cref="PartialFiles"/> ledger, kept in the operator's own config folder.</para>
/// </summary>
public sealed class ViewerFileClient : IDisposable
{
    private readonly MessageChannel _channel;
    private readonly PartialFiles _partials;

    private int _nextRequestId;

    /// <summary>One-shot replies, waiting to be matched to the request that asked for them.</summary>
    private readonly ConcurrentDictionary<int, TaskCompletionSource<DirListReply>> _listWaiters = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<FileAccessReply>> _accessWaiters = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<FileSendReply>> _sendWaiters = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<FileSendResult>> _resultWaiters = new();

    /// <summary>The download in progress, if any. One at a time, like the host's side.</summary>
    private volatile Download? _download;

    /// <summary>What this link has been measured to carry, for sizing upload chunks. See FileChunkSize.</summary>
    private double _rateEstimate;

    private sealed class Download
    {
        public required int RequestId { get; init; }
        public required Channel<FileChunk> Chunks { get; init; }
        public required TaskCompletionSource<FileGetEnd> Finished { get; init; }
    }

    public ViewerFileClient(MessageChannel channel, PartialFiles partials)
    {
        _channel = channel;
        _partials = partials;
    }

    /// <summary>True while the person at the other end has agreed to file access on this connection.</summary>
    public bool Allowed { get; private set; }

    /// <summary>
    /// Hands a received message to this client. Returns true if it was a file message and has been
    /// dealt with, so the caller's switch does not need to know about any of them.
    /// </summary>
    public bool TryHandle(MessageType type, byte[] payload)
    {
        try
        {
            switch (type)
            {
                case MessageType.FileAccessReply:
                    Complete(_accessWaiters, FileAccessReply.FromBytes(payload), r => r.RequestId);
                    return true;

                case MessageType.DirListReply:
                    Complete(_listWaiters, DirListReply.FromBytes(payload), r => r.RequestId);
                    return true;

                case MessageType.FileChunk:
                {
                    var chunk = FileChunk.FromBytes(payload);
                    var current = _download;
                    if (current is not null && current.RequestId == chunk.RequestId)
                        current.Chunks.Writer.TryWrite(chunk);
                    return true;
                }

                case MessageType.FileGetEnd:
                {
                    var end = FileGetEnd.FromBytes(payload);
                    var current = _download;
                    if (current is not null && current.RequestId == end.RequestId)
                    {
                        current.Chunks.Writer.TryComplete();
                        current.Finished.TrySetResult(end);
                    }
                    return true;
                }

                case MessageType.FileSendReply:
                    Complete(_sendWaiters, FileSendReply.FromBytes(payload), r => r.RequestId);
                    return true;

                case MessageType.FileSendResult:
                    Complete(_resultWaiters, FileSendResult.FromBytes(payload), r => r.RequestId);
                    return true;

                default:
                    return false;
            }
        }
        catch (InvalidDataException)
        {
            // A malformed file message is dropped. It must never take the video down with it, and
            // the request that was waiting will time out or be cancelled by the operator.
            return true;
        }
    }

    private static void Complete<T>(ConcurrentDictionary<int, TaskCompletionSource<T>> waiters, T value, Func<T, int> id)
    {
        // An answer nobody is waiting for is discarded, not drawn. That is the whole point of the
        // request number: the operator may have moved on.
        if (waiters.TryRemove(id(value), out var waiter)) waiter.TrySetResult(value);
    }

    // ---------------------------------------------------------------- asking

    /// <summary>
    /// Asks the person at the other end whether their files may be looked at. Their answer is
    /// remembered for this connection only — this object dies with the socket, so a reconnect asks
    /// again, deliberately.
    /// </summary>
    public async Task<FileAccessReply> RequestAccessAsync(CancellationToken ct)
    {
        int id = NextId();
        var reply = await AskAsync(_accessWaiters, id, MessageType.FileAccessRequest,
            new FileAccessRequest(id).ToBytes(), ct).ConfigureAwait(false);

        Allowed = reply.Granted;
        return reply;
    }

    /// <summary>One page of a folder. An empty path asks for the machine's drives.</summary>
    public Task<DirListReply> ListAsync(string path, int skip, CancellationToken ct)
    {
        int id = NextId();
        return AskAsync(_listWaiters, id, MessageType.DirListRequest,
            new DirListRequest(id, path, skip).ToBytes(), ct);
    }

    // ---------------------------------------------------------------- copying a file here

    /// <summary>
    /// Copies a remote file to <paramref name="destinationFolder"/> on this machine, under
    /// <paramref name="saveAs"/>. Written to a temporary name and renamed only when the last byte
    /// has arrived, so a dropped link never leaves a half file wearing the real name.
    /// </summary>
    public async Task<FileGetEnd> DownloadAsync(
        string remotePath, string destinationFolder, string saveAs,
        IProgress<long>? progress, CancellationToken ct)
    {
        int id = NextId();
        var download = new Download
        {
            RequestId = id,
            Chunks = Channel.CreateUnbounded<FileChunk>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }),
            Finished = new TaskCompletionSource<FileGetEnd>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        _download = download;

        string temp = _partials.Begin(destinationFolder);
        long written = 0;

        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, useAsync: true))
            {
                await _channel.SendAsync(MessageType.FileGetRequest,
                    new FileGetRequest(id, remotePath, 0).ToBytes(), ct).ConfigureAwait(false);

                // Drains chunks until the host says the transfer is over, one way or another. The
                // channel is completed by the FileGetEnd, so this ends without needing a timeout.
                await foreach (var chunk in download.Chunks.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await file.WriteAsync(chunk.Bytes, ct).ConfigureAwait(false);
                    written += chunk.Bytes.Length;
                    progress?.Report(written);
                }

                await file.FlushAsync(ct).ConfigureAwait(false);
            }

            var end = await download.Finished.Task.WaitAsync(ct).ConfigureAwait(false);

            // Only a transfer the host called complete, and whose bytes we actually have, is allowed
            // to take the real name. Anything else is litter and is removed.
            if (end.Status != FileStatus.Ok || written != end.TotalBytes)
            {
                Discard(temp);
                return end.Status == FileStatus.Ok
                    ? new FileGetEnd(id, FileStatus.ReadError, written,
                        "The file did not arrive complete, so it was not kept.")
                    : end;
            }

            string finalPath = Path.Combine(destinationFolder, saveAs);
            try
            {
                File.Move(temp, finalPath, overwrite: false);
            }
            catch (IOException)
            {
                Discard(temp);
                return new FileGetEnd(id, FileStatus.NotAllowed, written,
                    $"There is already a file called {saveAs} in that folder here.");
            }

            _partials.Finished(temp);
            return end;
        }
        catch (OperationCanceledException)
        {
            // Tell the other machine to stop reading, or it goes on sending a file nobody wants.
            TryCancelRemote(id);
            Discard(temp);
            return new FileGetEnd(id, FileStatus.Cancelled, written, "Stopped.");
        }
        catch (Exception ex)
        {
            TryCancelRemote(id);
            Discard(temp);
            return new FileGetEnd(id, FileStatus.WriteError, written, ex.Message);
        }
        finally
        {
            _download = null;
        }
    }

    // ---------------------------------------------------------------- putting a file there

    /// <summary>
    /// Sends a local file to a folder on the other machine. The reply carries the name it was
    /// actually saved as, which may differ if the person there chose "keep both".
    /// </summary>
    public async Task<(FileSendReply Reply, FileSendResult? Result)> UploadAsync(
        string localPath, string remoteFolder, IProgress<long>? progress, CancellationToken ct)
    {
        int id = NextId();

        FileStream source;
        long total;
        try
        {
            source = new FileStream(localPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, useAsync: true);
            total = source.Length;
        }
        catch (Exception ex)
        {
            return (new FileSendReply(id, FileStatus.ReadError,
                $"That file could not be read on this computer: {ex.Message}", string.Empty), null);
        }

        await using (source)
        {
            string name = Path.GetFileName(localPath);

            // The result waiter is registered BEFORE the request goes out. The other machine can
            // refuse and report at any point after this, including while the first chunk is still
            // being read off this disk.
            var resultWaiter = Register(_resultWaiters, id);

            var reply = await AskAsync(_sendWaiters, id, MessageType.FileSendRequest,
                new FileSendRequest(id, remoteFolder, name, total).ToBytes(), ct).ConfigureAwait(false);

            if (reply.Status != FileStatus.Ok)
            {
                _resultWaiters.TryRemove(id, out _);
                return (reply, null);
            }

            long offset = 0;
            var timer = new Stopwatch();

            try
            {
                while (offset < total)
                {
                    if (resultWaiter.Task.IsCompleted) break; // the other end gave up or refused

                    // Sized from what this link has been measured to carry, every time round. A
                    // fixed chunk holds the send lock long enough on a slow uplink to make the
                    // governor drop the picture — see FileChunkSize.
                    var buffer = new byte[FileChunkSize.ForMeasuredRate(_rateEstimate)];
                    int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read == 0) break;

                    var bytes = new FileChunk(id, offset, buffer.AsSpan(0, read).ToArray()).ToBytes();

                    timer.Restart();
                    await _channel.SendAsync(MessageType.FileSendChunk, bytes, ct).ConfigureAwait(false);
                    timer.Stop();

                    RecordSend(bytes.Length, timer.Elapsed.TotalMilliseconds);

                    offset += read;
                    progress?.Report(offset);
                }

                var result = await resultWaiter.Task.WaitAsync(ct).ConfigureAwait(false);
                return (reply, result);
            }
            catch (OperationCanceledException)
            {
                TryCancelRemote(id);
                _resultWaiters.TryRemove(id, out _);
                return (reply, new FileSendResult(id, FileStatus.Cancelled, offset, "Stopped."));
            }
            catch (Exception ex)
            {
                TryCancelRemote(id);
                _resultWaiters.TryRemove(id, out _);
                return (reply, new FileSendResult(id, FileStatus.ReadError, offset, ex.Message));
            }
        }
    }

    // ---------------------------------------------------------------- plumbing

    private int NextId() => Interlocked.Increment(ref _nextRequestId);

    private static TaskCompletionSource<T> Register<T>(ConcurrentDictionary<int, TaskCompletionSource<T>> waiters, int id)
    {
        var waiter = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiters[id] = waiter;
        return waiter;
    }

    private async Task<T> AskAsync<T>(
        ConcurrentDictionary<int, TaskCompletionSource<T>> waiters, int id,
        MessageType type, byte[] payload, CancellationToken ct)
    {
        var waiter = Register(waiters, id);
        try
        {
            await _channel.SendAsync(type, payload, ct).ConfigureAwait(false);
            return await waiter.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Never leave a waiter behind: the operator may ask the same thing again, and a stale
            // entry would make the second answer look like the first.
            waiters.TryRemove(id, out _);
            throw;
        }
    }

    /// <summary>
    /// Same arithmetic as the host's, for the same reason. The viewer has no bandwidth governor —
    /// it does not send the picture — so it keeps the one number chunk sizing needs.
    /// </summary>
    private void RecordSend(int bytes, double sendMs)
    {
        if (sendMs < 15 || bytes < 8 * 1024) return; // returned before the buffer filled: timed nothing

        double sample = bytes / (sendMs / 1000.0);
        _rateEstimate = _rateEstimate <= 0 ? sample : (_rateEstimate * 0.7) + (sample * 0.3);
    }

    private void TryCancelRemote(int requestId)
    {
        try { _ = _channel.SendAsync(MessageType.FileGetCancel, new FileGetCancel(requestId).ToBytes()); }
        catch { /* the link is already gone */ }
    }

    private void Discard(string temp)
    {
        try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        _partials.Finished(temp);
    }

    public void Dispose()
    {
        _download?.Chunks.Writer.TryComplete();
        // Completing the channel only ends the chunk-reading loop in DownloadAsync; it then still
        // awaits download.Finished, which nothing else was ever going to complete once the link is
        // gone. Left out, a disconnect mid-download hung the progress bar forever with no error —
        // exactly the "something stopped and nobody was told" class of bug this project treats as
        // worst, and asymmetric with UploadAsync, whose result waiter IS one of the four below.
        _download?.Finished.TrySetException(new IOException("The connection ended."));

        // Anything still waiting is woken with a failure rather than left hanging: a panel waiting
        // on a reply that can never come would sit there for the rest of the session.
        Fail(_accessWaiters);
        Fail(_listWaiters);
        Fail(_sendWaiters);
        Fail(_resultWaiters);

        static void Fail<T>(ConcurrentDictionary<int, TaskCompletionSource<T>> waiters)
        {
            foreach (var (_, waiter) in waiters)
                waiter.TrySetException(new IOException("The connection ended."));
            waiters.Clear();
        }
    }
}
