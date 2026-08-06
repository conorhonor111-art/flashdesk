using System.Diagnostics;
using System.Threading.Channels;
using RemoteDesktop.Host.Net;
using RemoteDesktop.Shared.Files;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host.Files;

/// <summary>Asks the person at this machine whether a file may be put on it. False means no.</summary>
public delegate Task<bool> AskIncomingFile(string callerId, string name, long bytes, string folder, bool isProgram);

/// <summary>Asks the person at this machine what to do about a file that is already there.</summary>
public delegate Task<ReplaceChoice> AskReplaceFile(string callerId, string name, string folder);

/// <summary>A file finished arriving. <paramref name="isProgram"/> gets its own line in the log.</summary>
public delegate void FileArrived(string callerId, string name, long bytes, string folder, bool isProgram, bool replaced);

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

    /// <summary>The three things the WRITE side needs. All null-safe: a missing ask means refuse.</summary>
    private readonly AskIncomingFile? _askIncoming;
    private readonly AskReplaceFile? _askReplace;
    private readonly FileArrived? _fileArrived;
    private readonly PartialFiles _partials;

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
        PartialFiles partials,
        Func<string, Task<bool>>? askAllowed,
        Action<string, string, long, string>? fileSent,
        AskIncomingFile? askIncoming = null,
        AskReplaceFile? askReplace = null,
        FileArrived? fileArrived = null)
    {
        _channel = channel;
        _governor = governor;
        _callerId = callerId;
        _partials = partials;
        _askAllowed = askAllowed;
        _fileSent = fileSent;
        _askIncoming = askIncoming;
        _askReplace = askReplace;
        _fileArrived = fileArrived;
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

            case MessageType.FileSendRequest:
                Spawn(() => AnswerSendAsync(payload, ct));
                return true;

            case MessageType.FileSendChunk:
                // Routed inline — it is a queue push, nothing more. The bytes are WRITTEN on the
                // upload's own task; this thread never touches the disk.
                TakeChunk(payload);
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

    // ---------------------------------------------------------------- put a file on this machine

    /// <summary>
    /// Most bytes that may sit in memory waiting to be written. It is a MEMORY bound, not a speed
    /// one: the sender can push faster than a disk accepts, and without a limit a slow or sleeping
    /// drive would turn into unbounded growth on the client's machine. In practice a home uplink is
    /// far slower than any disk and this is never reached — which is exactly why it must be a hard
    /// failure rather than a silent drop if it ever is.
    /// </summary>
    private const long MaxBufferedUploadBytes = 8L * 1024 * 1024;

    /// <summary>Kept clear of the last of the disk: filling a stranger's drive is its own damage.</summary>
    private const long FreeSpaceMargin = 32L * 1024 * 1024;

    /// <summary>One upload at a time, held while it runs. Null between uploads.</summary>
    private volatile Upload? _upload;

    /// <summary>Asked once per connection: may they put files here at all.</summary>
    private readonly SemaphoreSlim _uploadConsentGate = new(1, 1);
    private bool _uploadAsked;
    private bool _uploadAllowed;

    private sealed class Upload
    {
        public required int RequestId { get; init; }
        public required long Expected { get; init; }
        public required Channel<byte[]> Chunks { get; init; }
        public long Queued;      // bytes accepted from the wire, written or not
        public long Buffered;    // bytes accepted and not yet written
        public volatile bool Overrun;
    }

    /// <summary>
    /// Pushes an arriving chunk onto the upload's queue. Runs on the inbound loop, so it does three
    /// cheap things and returns: parse, check it belongs to the upload in progress, queue.
    /// </summary>
    private void TakeChunk(byte[] payload)
    {
        var current = _upload;
        if (current is null) return; // no upload agreed: the bytes are dropped, not written

        FileChunk chunk;
        try { chunk = FileChunk.FromBytes(payload); }
        catch { return; }

        if (chunk.RequestId != current.RequestId) return;

        // A sender that jumps around is broken or is trying something. Only the next bytes in
        // sequence are accepted; writing at an offset the sender chose would let one upload scatter
        // bytes through a file that is already there.
        if (chunk.Offset != current.Queued) { current.Overrun = true; current.Chunks.Writer.TryComplete(); return; }

        if (Interlocked.Read(ref current.Buffered) + chunk.Bytes.Length > MaxBufferedUploadBytes)
        {
            current.Overrun = true;
            current.Chunks.Writer.TryComplete();
            return;
        }

        if (!current.Chunks.Writer.TryWrite(chunk.Bytes)) return;
        Interlocked.Add(ref current.Buffered, chunk.Bytes.Length);
        current.Queued += chunk.Bytes.Length;
    }

    private async Task AnswerSendAsync(byte[] payload, CancellationToken ct)
    {
        var request = FileSendRequest.FromBytes(payload);

        // Reading and writing are DIFFERENT permissions and are asked separately. Agreeing to be
        // looked at is not agreeing to be written to, so the read consent above grants nothing here.
        if (Interlocked.CompareExchange(ref _transferBusy, 1, 0) != 0)
        {
            await SendReplyAsync(request.RequestId, FileStatus.Busy,
                "That computer is already moving a file.", ct).ConfigureAwait(false);
            return;
        }

        try { await ReceiveFileAsync(request, ct).ConfigureAwait(false); }
        finally { Interlocked.Exchange(ref _transferBusy, 0); }
    }

    private async Task ReceiveFileAsync(FileSendRequest request, CancellationToken ct)
    {
        // 1. The path rules. The folder is checked as a path, the NAME as a bare name, and the join
        //    is proved to still be inside the folder — on the RESOLVED path, so C:\Temp2 cannot pass
        //    a check for C:\Temp.
        if (!RemotePath.TryResolveInside(request.Folder, request.Name, out string? full, out string? problem)
            || !RemotePath.TryResolve(request.Folder, out string? folder, out problem)
            || !LocalDrives.IsOnLocalDrive(folder!, out problem))
        {
            await SendReplyAsync(request.RequestId, FileStatus.NotAllowed, problem!, ct).ConfigureAwait(false);
            return;
        }

        if (!Directory.Exists(folder!))
        {
            await SendReplyAsync(request.RequestId, FileStatus.NotFound,
                "That folder is not on that computer any more.", ct).ConfigureAwait(false);
            return;
        }

        if (!HasRoomFor(folder!, request.TotalBytes))
        {
            await SendReplyAsync(request.RequestId, FileStatus.NoRoom,
                "There is not enough room on that computer's disk for this file.", ct).ConfigureAwait(false);
            return;
        }

        bool isProgram = RemotePath.LooksExecutable(request.Name);

        // 2. The person decides. Once per connection for files in general; EVERY time for a program,
        //    by name — see IncomingFileDialog for why that is not the same question.
        if (!await AllowedToWriteAsync(request, folder!, isProgram, ct).ConfigureAwait(false))
        {
            await SendReplyAsync(request.RequestId, FileStatus.RefusedByPerson,
                "They said no to putting that file on their computer.", ct).ConfigureAwait(false);
            return;
        }

        // 3. Their file, their question. Never the operator's.
        string finalPath = full!;
        string savedAs = request.Name;
        bool replacing = false;

        if (File.Exists(finalPath) || Directory.Exists(finalPath))
        {
            ReplaceChoice choice;
            try { choice = _askReplace is null ? ReplaceChoice.Refuse : await _askReplace(_callerId, request.Name, folder!).ConfigureAwait(false); }
            catch { choice = ReplaceChoice.Refuse; }

            switch (choice)
            {
                case ReplaceChoice.Replace:
                    replacing = true;
                    break;

                case ReplaceChoice.KeepBoth:
                    if (!TryFreeName(folder!, request.Name, out string? free, out string? why))
                    {
                        await SendReplyAsync(request.RequestId, FileStatus.NameTaken, why!, ct).ConfigureAwait(false);
                        return;
                    }
                    savedAs = free!;
                    RemotePath.TryResolveInside(folder!, savedAs, out finalPath!, out _);
                    break;

                default:
                    await SendReplyAsync(request.RequestId, FileStatus.RefusedByPerson,
                        "They chose to keep the file they already had.", ct).ConfigureAwait(false);
                    return;
            }
        }

        // 4. The partial. Recorded BEFORE it is created, and in the DESTINATION folder so the
        //    finishing rename stays inside one volume. See PartialFiles.
        string temp = _partials.Begin(folder!);
        FileStream file;
        try
        {
            // CreateNew, so a name collision is an error rather than a silent overwrite.
            // FileShare.None, so a second copy of FlashDesk starting mid-transfer cannot sweep this
            // very file away — its delete is refused by Windows and the entry stays for next time.
            file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, useAsync: true);
        }
        catch (Exception)
        {
            _partials.Finished(temp);
            await SendReplyAsync(request.RequestId, FileStatus.WriteError,
                "That computer would not let the file be written there.", ct).ConfigureAwait(false);
            return;
        }

        // 5. THE RE-CHECK, on the handle, after the open. A junction at the destination redirects a
        //    write, and no string check can see it — see OpenedPath. Done before a single byte is
        //    accepted, so nothing has been written anywhere by the time it can fail.
        if (!OpenedPath.IsInsideFolder(file.SafeFileHandle, folder!, out string? elsewhere))
        {
            file.Dispose();
            TryDelete(temp);
            _partials.Finished(temp);
            await SendReplyAsync(request.RequestId, FileStatus.NotAllowed, elsewhere!, ct).ConfigureAwait(false);
            return;
        }

        var upload = new Upload
        {
            RequestId = request.RequestId,
            Expected = request.TotalBytes,
            Chunks = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }),
        };
        _upload = upload;

        // Only NOW is the sender told to start. Everything that could refuse has already run, so a
        // "yes" is never followed by bytes arriving into a decision that had not been made.
        await SendReplyAsync(request.RequestId, FileStatus.Ok, string.Empty, ct, savedAs).ConfigureAwait(false);

        try
        {
            await WriteChunksAsync(upload, file, temp, finalPath, folder!, savedAs, replacing, isProgram, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _upload = null;
        }
    }

    private async Task WriteChunksAsync(
        Upload upload, FileStream file, string temp, string finalPath, string folder,
        string savedAs, bool replacing, bool isProgram, CancellationToken ct)
    {
        long written = 0;
        FileStatus status;
        string message;

        try
        {
            using (file)
            {
                while (written < upload.Expected)
                {
                    if (ct.IsCancellationRequested || _dead.IsCancellationRequested)
                    { status = FileStatus.Cancelled; message = "The connection ended."; goto failed; }

                    if (_cancelledRequestId == upload.RequestId)
                    { status = FileStatus.Cancelled; message = "Stopped."; goto failed; }

                    if (upload.Overrun)
                    { status = FileStatus.WriteError; message = "The file arrived faster than that computer could write it."; goto failed; }

                    byte[] chunk;
                    try
                    {
                        // A timeout rather than an indefinite wait: a sender that simply stops must
                        // not leave a partial file and a held handle on someone's disk forever.
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct, _dead.Token);
                        idle.CancelAfter(TimeSpan.FromSeconds(60));
                        chunk = await upload.Chunks.Reader.ReadAsync(idle.Token).ConfigureAwait(false);
                    }
                    catch (ChannelClosedException)
                    { status = FileStatus.WriteError; message = "The file stopped arriving before it was complete."; goto failed; }
                    catch (OperationCanceledException)
                    {
                        status = FileStatus.Cancelled;
                        message = ct.IsCancellationRequested || _dead.IsCancellationRequested
                            ? "The connection ended."
                            : "Nothing more arrived, so the file was not kept.";
                        goto failed;
                    }

                    Interlocked.Add(ref upload.Buffered, -chunk.Length);

                    // More bytes than were declared. Refused rather than written: the size was the
                    // basis for the free-space check and for what the person was shown.
                    if (written + chunk.Length > upload.Expected)
                    { status = FileStatus.WriteError; message = "More arrived than was promised, so the file was not kept."; goto failed; }

                    try { await file.WriteAsync(chunk, ct).ConfigureAwait(false); }
                    catch (IOException)
                    { status = FileStatus.NoRoom; message = "That computer ran out of room while the file was arriving."; goto failed; }
                    catch (Exception)
                    { status = FileStatus.WriteError; message = "The file could not be written to that computer's disk."; goto failed; }

                    written += chunk.Length;
                }

                await file.FlushAsync(ct).ConfigureAwait(false);
            }

            // 6. Into place, and only now. Every byte is on the disk and the count matches what was
            //    promised, which is what makes the rename safe to be the moment the real name appears.
            try
            {
                File.Move(temp, finalPath, overwrite: replacing);
            }
            catch (IOException)
            {
                // The destination appeared between the question and this moment. The safe outcome:
                // nothing of theirs is destroyed, and the operator is told why.
                TryDelete(temp);
                _partials.Finished(temp);
                await SendResultAsync(upload.RequestId, FileStatus.NameTaken, written,
                    "A file with that name appeared on that computer before this one could be saved.", ct).ConfigureAwait(false);
                return;
            }

            _partials.Finished(temp);
            await SendResultAsync(upload.RequestId, FileStatus.Ok, written, string.Empty, ct).ConfigureAwait(false);

            try { _fileArrived?.Invoke(_callerId, savedAs, written, folder, isProgram, replacing); }
            catch { /* a log that cannot be written must never fail a transfer */ }
            return;
        }
        catch (Exception)
        {
            status = FileStatus.WriteError;
            message = "The file could not be saved on that computer.";
        }

    failed:
        // ONE exit for every failure, and it always ends the same way: no half file, no half file
        // wearing the real name, no forgotten entry in the ledger, and a sentence for the operator.
        try { file.Dispose(); } catch { }
        TryDelete(temp);
        _partials.Finished(temp);
        await SendResultAsync(upload.RequestId, status, written, message, ct).ConfigureAwait(false);
    }

    private async Task<bool> AllowedToWriteAsync(FileSendRequest request, string folder, bool isProgram, CancellationToken ct)
    {
        await _uploadConsentGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_uploadAsked)
            {
                _uploadAsked = true;
                try
                {
                    _uploadAllowed = _askIncoming is not null
                        && await _askIncoming(_callerId, request.Name, request.TotalBytes, folder, isProgram).ConfigureAwait(false);
                }
                catch { _uploadAllowed = false; }

                // That question already named this file, so a program asked about here is not asked
                // about twice in a row.
                return _uploadAllowed;
            }

            if (!_uploadAllowed) return false;

            // ⚠ A PROGRAM IS ASKED ABOUT EVERY TIME, by name. Without this, the first upload could
            // be a text file and every executable afterwards would arrive in silence — which is
            // precisely the step a tech-support scam needs. Reversing it is one line, and it is the
            // one place this goes beyond the written plan.
            if (!isProgram) return true;

            try
            {
                return _askIncoming is not null
                    && await _askIncoming(_callerId, request.Name, request.TotalBytes, folder, true).ConfigureAwait(false);
            }
            catch { return false; }
        }
        finally { _uploadConsentGate.Release(); }
    }

    /// <summary>
    /// "Report.docx" -> "Report (2).docx", the first number that is actually free. Every candidate
    /// goes back through the same name and containment rules as the original: a stem near the
    /// 255-character limit grows when a number is added, and a name that no longer passes must fail
    /// rather than be trimmed into one that does.
    /// </summary>
    private static bool TryFreeName(string folder, string name, out string? chosen, out string? problem)
    {
        chosen = null;
        problem = null;

        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);

        for (int i = 2; i <= 99; i++)
        {
            string candidate = $"{stem} ({i}){extension}";
            if (!RemotePath.TryResolveInside(folder, candidate, out string? full, out _)) continue;
            if (File.Exists(full) || Directory.Exists(full)) continue;

            chosen = candidate;
            return true;
        }

        problem = "There were already too many files with that name, so nothing was saved.";
        return false;
    }

    private static bool HasRoomFor(string folder, long bytes)
    {
        try
        {
            string? root = Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(root)) return false;
            return new DriveInfo(root).AvailableFreeSpace > bytes + FreeSpaceMargin;
        }
        catch
        {
            // A drive that will not say how full it is: let the write itself find out rather than
            // refusing a transfer that might be perfectly fine.
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private Task SendReplyAsync(int requestId, FileStatus status, string message, CancellationToken ct, string savedAs = "") =>
        SendAsync(MessageType.FileSendReply, new FileSendReply(requestId, status, message, savedAs).ToBytes(), ct);

    private Task SendResultAsync(int requestId, FileStatus status, long written, string message, CancellationToken ct) =>
        SendAsync(MessageType.FileSendResult, new FileSendResult(requestId, status, written, message).ToBytes(), ct);

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
        // Wakes the upload's reader immediately rather than leaving it on its idle timeout, so the
        // partial file is deleted when the session ends instead of at the next start-up sweep.
        try { _upload?.Chunks.Writer.TryComplete(); } catch { }
        _dead.Dispose();
        _consentGate.Dispose();
        _uploadConsentGate.Dispose();
    }
}
