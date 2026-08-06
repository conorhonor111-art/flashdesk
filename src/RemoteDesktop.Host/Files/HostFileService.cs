using System.Diagnostics;
using RemoteDesktop.Host.Net;
using RemoteDesktop.Shared.Files;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host.Files;

/// <summary>
/// Answers the operator's file requests on the client's machine: may I look, what is in this folder,
/// send me this file, stop. One of these exists per connection and dies with it.
///
/// <para><b>EVERY REQUEST RUNS OFF THE MESSAGE LOOP, and that is not a performance nicety.</b> The
/// inbound loop that delivers these messages is the same loop that answers the latency ping and
/// injects the operator's mouse and keyboard. Listing <c>C:\Windows\WinSxS</c> on a tired laptop
/// disk takes seconds; doing it on that thread would freeze the remote mouse and, because the ping
/// would be stuck behind it, would have the bandwidth governor conclude the link had collapsed and
/// drop the picture. So the loop copies the payload, hands the work to a task and returns
/// immediately.</para>
///
/// <para><b>Consent is held HERE, per connection, and is discarded with the socket.</b> It is
/// deliberately not stored with the caller's number: <c>HostServer</c> grants a 90-second grace in
/// which the same number may resume a dropped session with no dialog, which is right for the screen
/// and wrong for files — an operator could otherwise drop the link on purpose and come straight back
/// to regain file access without being asked.</para>
///
/// <para><b>What this can do is deliberately narrow: LOOK and COPY OFF.</b> No delete, no rename, no
/// move, no new folders. The operator already has the mouse and can do all of those on screen where
/// the client can watch; a file API for them would add no power, only concealment.</para>
/// </summary>
internal sealed class HostFileService : IDisposable
{
    /// <summary>What the transfer loop checks between chunks, so a Cancel is felt within one chunk.</summary>
    private readonly CancellationTokenSource _dead = new();

    private readonly MessageChannel _channel;
    private readonly BandwidthGovernor _governor;
    private readonly string _callerId;

    /// <summary>Asks the person at this machine. Null means refuse — a wiring mistake fails closed.</summary>
    private readonly Func<string, Task<bool>>? _askAllowed;

    /// <summary>Told when a file actually left this machine: name, bytes, the folder it came from.</summary>
    private readonly Action<string, string, long, string>? _fileSent;

    /// <summary>0 = free, 1 = in use. One listing and one transfer at a time, no more.</summary>
    private int _listingBusy;
    private int _transferBusy;

    /// <summary>Guards the consent question itself, so a repeated request cannot raise two dialogs.</summary>
    private readonly SemaphoreSlim _consentGate = new(1, 1);
    private bool _asked;
    private bool _allowed;

    /// <summary>The transfer the operator has asked to stop, or int.MinValue for none.</summary>
    private volatile int _cancelledRequestId = int.MinValue;

    internal HostFileService(
        MessageChannel channel,
        BandwidthGovernor governor,
        string callerId,
        Func<string, Task<bool>>? askAllowed,
        Action<string, string, long, string>? fileSent)
    {
        _channel = channel;
        _governor = governor;
        _callerId = callerId;
        _askAllowed = askAllowed;
        _fileSent = fileSent;
    }

    /// <summary>
    /// True if this message was a file message and has been taken. Called from the inbound loop and
    /// returns at once — nothing here touches the disk on the caller's thread.
    /// </summary>
    internal bool TryHandle(MessageType type, byte[] payload, CancellationToken ct)
    {
        switch (type)
        {
            case MessageType.FileAccessRequest:
                Spawn(() => AnswerAccessAsync(payload, ct));
                return true;

            case MessageType.DirListRequest:
                Spawn(() => AnswerListAsync(payload, ct));
                return true;

            case MessageType.FileGetRequest:
                Spawn(() => AnswerGetAsync(payload, ct));
                return true;

            case MessageType.FileGetCancel:
                // Handled inline: it is four bytes and setting a flag, and putting it on a task
                // would let it arrive AFTER the transfer it is meant to stop.
                try { _cancelledRequestId = FileGetCancel.FromBytes(payload).RequestId; }
                catch { /* malformed cancel: nothing to stop */ }
                return true;

            default:
                return false;
        }
    }

    // A malformed payload must never take the session down with it, so every spawned unit of work
    // is wrapped once, here, rather than trusting eight call sites to remember.
    private void Spawn(Func<Task> work) => _ = Task.Run(async () =>
    {
        try { await work().ConfigureAwait(false); }
        catch (OperationCanceledException) { /* the session ended underneath it */ }
        catch { /* a bad request is answered or dropped; it is never fatal */ }
    });

    // ---------------------------------------------------------------- may I look at your files?

    private async Task AnswerAccessAsync(byte[] payload, CancellationToken ct)
    {
        int id = FileAccessRequest.FromBytes(payload).RequestId;

        // One question per connection, however many times it is asked. The gate is held across the
        // dialog so a second request while it is on screen waits for the same answer rather than
        // stacking a second window on top of a person who is trying to read the first.
        await _consentGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_asked)
            {
                _asked = true;
                try { _allowed = _askAllowed is not null && await _askAllowed(_callerId).ConfigureAwait(false); }
                catch { _allowed = false; }
            }
        }
        finally { _consentGate.Release(); }

        await SendAsync(MessageType.FileAccessReply,
            new FileAccessReply(id, _allowed,
                _allowed ? string.Empty : "They said no to letting you look at the files on that computer.")
                .ToBytes(), ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- what is in this folder?

    private async Task AnswerListAsync(byte[] payload, CancellationToken ct)
    {
        var request = DirListRequest.FromBytes(payload);

        if (!_allowed)
        {
            await ListFailureAsync(request, FileStatus.NotAllowed,
                "Looking at the files on that computer has not been agreed to.", ct).ConfigureAwait(false);
            return;
        }

        if (Interlocked.CompareExchange(ref _listingBusy, 1, 0) != 0)
        {
            await ListFailureAsync(request, FileStatus.Busy,
                "That computer is still working on the last folder.", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            // An EMPTY path is the root of the browser: this machine's own drives. It is not a
            // folder and must not go through the path rules, which would rightly reject "".
            if (string.IsNullOrEmpty(request.Path))
            {
                var drives = LocalDrives.Roots();
                await SendAsync(MessageType.DirListReply,
                    new DirListReply(request.RequestId, FileStatus.Ok, string.Empty, 0, false, drives).ToBytes(),
                    ct).ConfigureAwait(false);
                return;
            }

            if (!RemotePath.TryResolve(request.Path, out string? folder, out string? problem)
                || !LocalDrives.IsOnLocalDrive(folder!, out problem))
            {
                await ListFailureAsync(request, FileStatus.NotAllowed, problem!, ct).ConfigureAwait(false);
                return;
            }

            var reply = ReadPage(request.RequestId, folder!, request.Skip);
            await SendAsync(MessageType.DirListReply, reply.ToBytes(), ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _listingBusy, 0);
        }
    }

    /// <summary>
    /// One page of a folder. Folders before files, always — a listing that mixes them reads as a
    /// mess whatever it is sorted by, and the panel cannot re-sort across pages honestly.
    ///
    /// <para><b>Paging is by SKIP over the file system's own order.</b> That order is stable on
    /// NTFS, which is what every Windows machine this will run on uses. It is not a snapshot: a
    /// folder that changes between two pages can show an entry twice or not at all. Fixing that
    /// properly means holding the whole listing in memory per connection, which is exactly the cost
    /// paging exists to avoid — and this is a support tool looking at a folder, not a backup
    /// program.</para>
    /// </summary>
    private static DirListReply ReadPage(int requestId, string folder, int skip)
    {
        try
        {
            var entries = new List<DirEntry>(DirListReply.PageSize);
            bool hasMore = false;
            int seen = 0;

            // Lazily enumerated, so a folder with six figures of entries costs only what is walked
            // past. Directories and files in two passes, because that is what puts folders first
            // without sorting anything.
            foreach (var (path, isDirectory) in Walk(folder))
            {
                if (seen++ < skip) continue;

                if (entries.Count == DirListReply.PageSize)
                {
                    // One entry past the page is all it takes to answer "is there more?" honestly.
                    // Counting the rest would mean walking WinSxS to produce a number nobody acts on.
                    hasMore = true;
                    break;
                }

                try
                {
                    FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
                    entries.Add(new DirEntry(
                        Path.GetFileName(path),
                        info is FileInfo file ? file.Length : 0,
                        isDirectory,
                        info.LastWriteTimeUtc.Ticks));
                }
                catch
                {
                    // An entry that vanished, or that refuses to say how big it is. Listed with what
                    // is known rather than dropped, so the operator sees the name exists.
                    entries.Add(new DirEntry(Path.GetFileName(path), 0, isDirectory, 0));
                }
            }

            return new DirListReply(requestId, FileStatus.Ok, string.Empty, skip, hasMore, entries);
        }
        catch (DirectoryNotFoundException)
        {
            return Failed(requestId, skip, FileStatus.NotFound, "That folder is not there any more.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(requestId, skip, FileStatus.AccessDenied,
                "Windows does not let that computer's user open this folder.");
        }
        catch (IOException)
        {
            return Failed(requestId, skip, FileStatus.ReadError, "That folder could not be read.");
        }
    }

    private static IEnumerable<(string Path, bool IsDirectory)> Walk(string folder)
    {
        foreach (string d in Directory.EnumerateDirectories(folder)) yield return (d, true);
        foreach (string f in Directory.EnumerateFiles(folder)) yield return (f, false);
    }

    private static DirListReply Failed(int requestId, int skip, FileStatus status, string message) =>
        new(requestId, status, message, skip, false, Array.Empty<DirEntry>());

    private Task ListFailureAsync(DirListRequest request, FileStatus status, string message, CancellationToken ct) =>
        SendAsync(MessageType.DirListReply,
            Failed(request.RequestId, request.Skip, status, message).ToBytes(), ct);

    // ---------------------------------------------------------------- send me this file

    private async Task AnswerGetAsync(byte[] payload, CancellationToken ct)
    {
        var request = FileGetRequest.FromBytes(payload);

        if (!_allowed)
        {
            await EndAsync(request.RequestId, FileStatus.NotAllowed, 0,
                "Looking at the files on that computer has not been agreed to.", ct).ConfigureAwait(false);
            return;
        }

        if (Interlocked.CompareExchange(ref _transferBusy, 1, 0) != 0)
        {
            await EndAsync(request.RequestId, FileStatus.Busy, 0,
                "That computer is already sending a file.", ct).ConfigureAwait(false);
            return;
        }

        try { await StreamFileAsync(request, ct).ConfigureAwait(false); }
        finally { Interlocked.Exchange(ref _transferBusy, 0); }
    }

    private async Task StreamFileAsync(FileGetRequest request, CancellationToken ct)
    {
        if (!RemotePath.TryResolve(request.Path, out string? path, out string? problem)
            || !LocalDrives.IsOnLocalDrive(path!, out problem))
        {
            await EndAsync(request.RequestId, FileStatus.NotAllowed, 0, problem!, ct).ConfigureAwait(false);
            return;
        }

        FileStream stream;
        try
        {
            // Shared as widely as Windows allows: a log file the program that writes it still has
            // open is exactly the file a support session needs, and refusing it would fail on the
            // ordinary case. Nothing here writes, so sharing costs nothing.
            stream = new FileStream(path!, FileMode.Open, FileAccess.Read,
                // bufferSize 1 = unbuffered: every read here is already a whole chunk, so a second
                // buffer underneath would copy megabytes for nothing.
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, useAsync: true);
        }
        catch (FileNotFoundException) { await EndAsync(request.RequestId, FileStatus.NotFound, 0, "That file is not there any more.", ct).ConfigureAwait(false); return; }
        catch (DirectoryNotFoundException) { await EndAsync(request.RequestId, FileStatus.NotFound, 0, "That folder is not there any more.", ct).ConfigureAwait(false); return; }
        catch (UnauthorizedAccessException) { await EndAsync(request.RequestId, FileStatus.AccessDenied, 0, "Windows does not let that computer's user open this file.", ct).ConfigureAwait(false); return; }
        catch (IOException) { await EndAsync(request.RequestId, FileStatus.InUse, 0, "Another program has that file open and will not share it.", ct).ConfigureAwait(false); return; }

        using (stream)
        {
            // ⚠ THE RE-CHECK, AND IT MUST BE HERE — after the open, on the HANDLE. Every rule above
            // this line looked at a string, and a junction is invisible in a string: a path that
            // passed every check can have opened a file on somebody's employer's server. Checking
            // the path again would leave the same race, because a junction can be swapped between
            // the check and the open. See OpenedPath.
            if (!OpenedPath.IsWhereWeMeant(stream.SafeFileHandle, path!, out string? wrongPlace))
            {
                await EndAsync(request.RequestId, FileStatus.NotAllowed, 0, wrongPlace!, ct).ConfigureAwait(false);
                return;
            }

            long offset = request.StartOffset;
            if (offset < 0 || offset > stream.Length)
            {
                await EndAsync(request.RequestId, FileStatus.ReadError, 0, "That file could not be read from there.", ct).ConfigureAwait(false);
                return;
            }
            if (offset > 0) stream.Seek(offset, SeekOrigin.Begin);

            long sent = 0;
            var sendTimer = new Stopwatch();

            while (true)
            {
                if (ct.IsCancellationRequested || _dead.IsCancellationRequested)
                {
                    await EndAsync(request.RequestId, FileStatus.Cancelled, sent, "The connection ended.", ct).ConfigureAwait(false);
                    return;
                }

                if (_cancelledRequestId == request.RequestId)
                {
                    await EndAsync(request.RequestId, FileStatus.Cancelled, sent, "Stopped.", ct).ConfigureAwait(false);
                    return;
                }

                // Sized from the measured link EVERY time round, not once at the start: the link is
                // re-measured as chunks go out, so a transfer that begins in ignorance corrects
                // itself within a few chunks. See FileChunkSize for why a fixed size blurs the
                // picture on a slow uplink.
                var buffer = new byte[_governor.FileChunkBytes];

                int read;
                try { read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false); }
                catch (IOException)
                {
                    await EndAsync(request.RequestId, FileStatus.ReadError, sent, "That file could not be read to the end.", ct).ConfigureAwait(false);
                    return;
                }

                if (read == 0) break;

                var chunk = new FileChunk(request.RequestId, offset, buffer.AsSpan(0, read).ToArray());
                var bytes = chunk.ToBytes();

                sendTimer.Restart();
                await SendAsync(MessageType.FileChunk, bytes, ct).ConfigureAwait(false);
                sendTimer.Stop();

                // Feeds the RATE ESTIMATE only, never the ladder. During a download of a still
                // screen the frames are too small to time anything, so these chunks are the only
                // measurement of the link there is — and a link being full is not a link in trouble.
                _governor.OnBulkSent(bytes.Length, sendTimer.Elapsed.TotalMilliseconds);

                offset += read;
                sent += read;
            }

            await EndAsync(request.RequestId, FileStatus.Ok, sent, string.Empty, ct).ConfigureAwait(false);

            // Logged only on a completed transfer, and only after the last byte is on the wire. A
            // line saying a file left this machine when it did not would be worse than no line.
            try { _fileSent?.Invoke(_callerId, Path.GetFileName(path!), sent, Path.GetDirectoryName(path!) ?? path!); }
            catch { /* a log that cannot be written must never fail a transfer */ }
        }
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>
    /// A transfer ALWAYS ends with one of these, however it ended. A transfer that simply stops is
    /// indistinguishable from a stalled one, and the operator would be left watching a bar that
    /// never moves with nothing to read.
    /// </summary>
    private Task EndAsync(int requestId, FileStatus status, long total, string message, CancellationToken ct) =>
        SendAsync(MessageType.FileGetEnd, new FileGetEnd(requestId, status, total, message).ToBytes(), ct);

    private async Task SendAsync(MessageType type, byte[] payload, CancellationToken ct)
    {
        try { await _channel.SendAsync(type, payload, ct).ConfigureAwait(false); }
        catch { /* the viewer is gone; the session loop is already unwinding */ }
    }

    public void Dispose()
    {
        try { _dead.Cancel(); } catch { }
        _dead.Dispose();
        _consentGate.Dispose();
    }
}
