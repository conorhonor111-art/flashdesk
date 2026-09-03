using System.Collections.Specialized;
using System.Linq;
using RemoteDesktop.Shared.Files;
using RemoteDesktop.Shared.Identity;
using RemoteDesktop.UI;

namespace RemoteDesktop.Viewer.Files;

/// <summary>
/// The operator's file panel: where to look, what is there, get a file, send a file, stop.
///
/// <para><b>THE FOCUS PROBLEM IS THE DESIGN PROBLEM HERE, and it is not about typing.</b> Every
/// useful control in this panel takes keyboard focus when it is clicked — a list, a button, a text
/// box, all of them. The moment focus leaves the video, <c>InputCapture</c> stops driving the remote
/// machine, which is correct and has always happened. What was missing is that NOBODY WAS TOLD. The
/// operator clicks a folder, then types, and the letters go nowhere; from where they are sitting the
/// other machine has frozen. So this panel does not try to avoid focus — it announces it.</para>
///
/// <para><b>What happens when focus enters this panel, in this order:</b> every key the viewer is
/// holding down is released on the remote machine FIRST, then forwarding stops, then a band appears
/// across the picture saying so. The order matters: stop forwarding first and the key-ups never
/// leave, and the remote machine is left with Ctrl or Alt held down.</para>
///
/// <para><b>THERE IS NO KEYBOARD SHORTCUT INTO THIS PANEL, deliberately.</b> Any combination
/// FlashDesk kept for itself would be a key the operator could never send to the person they are
/// helping — and the hole would be invisible: you press it, and nothing happens over there.
/// Ctrl+L was the obvious candidate and is exactly the wrong one, because a browser is open on
/// their machine too. The rule this settles: <b>while remote control is active, FlashDesk swallows
/// nothing.</b></para>
///
/// <para><b>Ctrl+C and Ctrl+V inside the list are NOT the shortcut the rule above forbids.</b> That
/// rule is about a key stolen from the remote machine — one the operator needed to send forward and
/// FlashDesk kept instead. Copy/paste here only ever fires while the LIST already has focus, which
/// means remote control is already suspended and nothing is being forwarded anyway (same as Enter,
/// already handled the same way). Nothing reaches into the panel from outside; it is local
/// navigation once the operator is already inside, exactly like a double-click.</para>
///
/// <para>Operator chrome, so it can never be mistaken for the client's window in a screenshot.</para>
/// </summary>
public sealed class FilePanel : UserControl
{
    /// <summary>Above this, Ctrl+C asks before fetching rather than starting silently — a stray
    /// keypress must not be able to start a multi-gigabyte transfer. Below it, ordinary documents,
    /// photos and spreadsheets pass through with no prompt at all, which is the whole point.</summary>
    private const long ClipboardAskAboveBytes = 100L * 1024 * 1024;

    private readonly ViewerFileClient _files;
    private readonly OperatorSettings _settings = new(FlashDeskFolder.Current);

    private readonly RoundedTextBox _path = new() { Font = Theme.Body, PlaceholderText = "C:\\Users" };
    private readonly Button _go = Theme.MakeButton("Go", ButtonKind.Neutral);
    private readonly Button _up = Theme.MakeButton("Up", ButtonKind.Neutral);
    private readonly Button _refresh = Theme.MakeButton("Refresh", ButtonKind.Neutral);
    private readonly ListView _list = new();
    private readonly Button _get = Theme.MakeButton("Copy to my computer", ButtonKind.Primary);
    private readonly Button _getSessionLog = Theme.MakeButton("Copy their session log to my computer", ButtonKind.Neutral);
    private readonly Button _send = Theme.MakeButton("Send a file…", ButtonKind.Neutral);
    private readonly Button _more = Theme.MakeButton("Show more", ButtonKind.Neutral);
    private readonly Button _stop = Theme.MakeButton("Stop", ButtonKind.Destructive);
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Continuous, Height = 6, Maximum = 1000 };

    // Feeds SessionWindow's status line — see "Screen is slowed while the file transfers" there.
    // The operator was never told WHY the picture goes soft during a transfer; now measured
    // nothing is being starved (PROGRESS.md, 2026-08-27), but 2 fps and 350 ms latency is still
    // genuinely unusable, and unexplained is what read as broken. Tracked here, not in
    // SessionWindow, because only this class knows a transfer is even happening.
    private long _transferStartedAtMs;

    /// <summary>Whether a download or upload is moving bytes right now.</summary>
    public bool IsTransferActive { get; private set; }

    /// <summary>
    /// Time left in the CURRENT transfer, from its own average rate so far — null until at least
    /// one progress sample has arrived, which is when a rate first exists to divide by.
    /// </summary>
    public TimeSpan? TransferTimeRemaining { get; private set; }
    private readonly Label _status = new()
    {
        Font = Theme.Body,
        ForeColor = Theme.OperatorHeaderText,
        AutoSize = false,
        Height = 40,
        Dock = DockStyle.Top,
    };

    /// <summary>Where the listing currently is. Empty means the root: the machine's own drives.</summary>
    private string _folder = string.Empty;
    private int _shown;
    private bool _hasMore;
    private CancellationTokenSource? _transfer;
    private bool _busy;

    /// <summary>Raised when focus enters or leaves this panel, so the session window can suspend
    /// remote control and say so on the picture. True = the operator is in here, not out there.</summary>
    public event Action<bool>? FocusHere;

    /// <summary>
    /// Raised when Escape is pressed anywhere in this panel. Found missing 2026-08-26, on the first
    /// real two-machine test — three people believed it already worked, and it never had. The
    /// session window handles this by focusing the picture, exactly as a click on it already does;
    /// this event only reports that Escape happened, on purpose, so the ACT of leaving stays defined
    /// in exactly one place (SessionWindow) rather than this panel reaching for a canvas it does not
    /// own.
    /// </summary>
    public event Action? EscapePressed;

    public FilePanel(ViewerFileClient files)
    {
        _files = files;

        BackColor = Theme.OperatorHeader;
        Padding = new Padding(Theme.S3);
        Width = 420;

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.Dock = DockStyle.Fill;
        _list.Font = Theme.Body;
        // BackColor was never set, so this defaulted to WinForms' plain white/system list
        // background — with every row's text set to OperatorHeaderText (a LIGHT colour meant for
        // the dark operator theme), rows were nearly invisible. Same pairing already used for
        // _status against this panel's own background, just never applied to the list itself.
        _list.BackColor = Theme.OperatorHeader;
        _list.ForeColor = Theme.OperatorHeaderText;
        // Widths sized to fit inside the panel's own 420 px width (minus padding and the list's own
        // scrollbar) — found live 2026-09-03: the old total (220+80+100=400) left no real margin,
        // and at the panel's actual on-screen width the header truncated to "me", the tail end of
        // "Name" scrolled into view instead of the start. Sized with real headroom this time, not
        // just under the same tight number.
        _list.Columns.Add("Name", 180);
        _list.Columns.Add("Size", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Changed", 90);
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C) { _ = CopySelectedToClipboardAsync(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.V) { _ = PasteFilesAsync(); e.Handled = true; }
        };

        _go.Click += (_, _) => _ = ShowFolderAsync(_path.Text.Trim(), 0);
        _up.Click += (_, _) => GoUp();
        _refresh.Click += (_, _) => _ = ShowFolderAsync(_folder, 0);
        _more.Click += (_, _) => _ = ShowFolderAsync(_folder, _shown);
        _get.Click += (_, _) => { if (Selected is { } entry) _ = DownloadEntryAsync(entry); else _status.Text = "Choose a file first."; };
        _getSessionLog.Click += (_, _) => _ = DownloadSessionLogAsync();
        _send.Click += (_, _) => _ = UploadAsync();
        _stop.Click += (_, _) => _transfer?.Cancel();

        // Enter on the path box means "go there" — the one keyboard convenience that costs the
        // remote machine nothing, because by the time it can be pressed the keyboard is already
        // ours: focus is in this panel and forwarding has already stopped.
        _path.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = ShowFolderAsync(_path.Text.Trim(), 0);
        };

        Controls.Add(BuildBody());
        Controls.Add(_status);
        Controls.Add(BuildPathRow());

        // Enter and Leave fire for the whole container, including every child — so this is one
        // place, not one per control, and a control added later cannot forget to do it.
        Enter += (_, _) => FocusHere?.Invoke(true);
        Leave += (_, _) => FocusHere?.Invoke(false);

        SetBusy(false);
        _status.Text = "Asking them for permission — their drives will appear here once they agree.";
    }

    private Control BuildPathRow()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        _path.Width = 240;
        _path.Margin = new Padding(0, 0, Theme.S2, Theme.S2);
        foreach (Control c in new Control[] { _path, _go, _up, _refresh })
        {
            c.Margin = new Padding(0, 0, Theme.S2, Theme.S2);
            row.Controls.Add(c);
        }
        return row;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, Theme.S2, 0, 0),
        };
        foreach (Control c in new Control[] { _get, _getSessionLog, _send, _more, _stop })
        {
            c.Margin = new Padding(0, 0, Theme.S2, Theme.S2);
            actions.Controls.Add(c);
        }

        _progress.Dock = DockStyle.Top;
        _progress.Margin = new Padding(0);

        body.Controls.Add(_list, 0, 0);
        body.Controls.Add(actions, 0, 1);
        body.Controls.Add(_progress, 0, 2);
        return body;
    }

    // ---------------------------------------------------------------- asking, once

    /// <summary>
    /// Asks the person at the other end, if they have not been asked on this connection. Called when
    /// the panel is first opened, so nothing is asked of them until the operator actually wants it.
    /// </summary>
    public async Task StartAsync()
    {
        if (_files.Allowed) return;

        _status.Text = "Waiting for them to answer — the question is on their screen now.";
        SetBusy(true);
        try
        {
            var reply = await _files.RequestAccessAsync(CancellationToken.None);
            if (!reply.Granted)
            {
                _status.Text = reply.Reason.Length > 0 ? reply.Reason : "They said no.";
                return;
            }
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            return;
        }
        finally { SetBusy(false); }

        await ShowFolderAsync(string.Empty, 0); // the root: their drives
    }

    // ---------------------------------------------------------------- looking

    private async Task ShowFolderAsync(string folder, int skip)
    {
        if (_busy) return;
        SetBusy(true);
        _status.Text = skip == 0 ? "Looking…" : "Getting more…";

        try
        {
            var reply = await _files.ListAsync(folder, skip, CancellationToken.None);
            if (reply.Status != FileStatus.Ok)
            {
                _status.Text = reply.Message.Length > 0 ? reply.Message : "That folder could not be opened.";
                return;
            }

            if (skip == 0)
            {
                _list.Items.Clear();
                _shown = 0;
                _folder = folder;
                _path.Text = folder;
            }

            foreach (var entry in reply.Entries) AddRow(entry);
            _shown += reply.Entries.Count;
            _hasMore = reply.HasMore;

            // ⚠ THE SENTENCE IS NOT OPTIONAL. Sorting is deliberately off while there are more
            // pages, because a window sorted within a page LOOKS like a sorted folder and is not —
            // and the operator would conclude a file is not there when it is on page three.
            _status.Text = _hasMore
                ? $"Showing the first {_shown:N0} — there are more. Not sorted while there are more pages."
                : _shown == 0
                    ? "This folder is empty. Type another path above and press Go, or press Up to go back."
                    : $"{_shown:N0} items. Folders first.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally { SetBusy(false); }
    }

    private void AddRow(DirEntry entry)
    {
        var row = new ListViewItem(entry.Name) { Tag = entry };
        row.SubItems.Add(entry.IsDirectory ? "" : Readable(entry.Size));
        row.SubItems.Add(entry.ModifiedUtcTicks > 0
            ? new DateTime(entry.ModifiedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd")
            : "");
        row.ForeColor = Theme.OperatorHeaderText;
        _list.Items.Add(row);
    }

    private DirEntry? Selected =>
        _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as DirEntry : null;

    private void OpenSelected()
    {
        var entry = Selected;
        if (entry is null) return;

        if (entry.IsDirectory)
        {
            // At the root the NAME IS THE PATH (the drives), so it is used as-is rather than joined
            // to an empty folder — see LocalDrives.Roots.
            string next = _folder.Length == 0 ? entry.Name : Path.Combine(_folder, entry.Name);
            _ = ShowFolderAsync(next, 0);
            return;
        }

        // A file: double-click fetches it, no dialog — see DownloadEntryAsync.
        _ = DownloadEntryAsync(entry);
    }

    private void GoUp()
    {
        if (_folder.Length == 0) return;

        string? parent = Path.GetDirectoryName(_folder.TrimEnd(Path.DirectorySeparatorChar));
        // No parent means we were at a drive root, so up goes to the list of drives.
        _ = ShowFolderAsync(string.IsNullOrEmpty(parent) ? string.Empty : parent, 0);
    }

    // ---------------------------------------------------------------- moving files

    /// <summary>
    /// Fetches one file to the LAST folder a download was saved to, asking only the first time
    /// there is nothing to remember yet. Shared by the "Copy to my computer" button and
    /// double-click, so both behave the same way and remember the same folder. Deliberately no
    /// per-download rename — that convenience is the direct cost of removing the per-download
    /// dialog, which is the whole point of this being asked for.
    /// </summary>
    private async Task DownloadEntryAsync(DirEntry entry)
    {
        // Not previously guarded because there was only ever one entry point (the button, which
        // SetBusy already disables). Double-click is a second entry point now, and a fast second
        // click while a transfer is running would overwrite _transfer out from under the first one
        // — the Stop button would then cancel the wrong transfer, and both finally-blocks would
        // race to clear the same field.
        if (_busy) return;
        if (entry.IsDirectory || _folder.Length == 0)
        {
            _status.Text = "Choose a file first.";
            return;
        }

        string? destination = _settings.LastDownloadFolder;
        if (destination is null || !Directory.Exists(destination))
        {
            using var pick = new FolderBrowserDialog
            {
                Description = "Where should downloads from this computer be saved? (Remembered for next time.)",
            };
            if (pick.ShowDialog(this) != DialogResult.OK) return;
            destination = pick.SelectedPath;
        }

        _transfer = new CancellationTokenSource();
        SetBusy(true);
        _progress.Value = 0;

        try
        {
            var end = await _files.DownloadAsync(
                Path.Combine(_folder, entry.Name), destination, entry.Name,
                Progress(entry.Size, "Copying…"), _transfer.Token);

            if (end.Status == FileStatus.Ok)
            {
                _settings.LastDownloadFolder = destination; // only remembered once it actually worked
                _status.Text = $"Copied {entry.Name} ({Readable(end.TotalBytes)}) to {destination}.";
            }
            else
            {
                _status.Text = end.Message.Length > 0 ? end.Message : "It did not finish.";
            }
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _progress.Value = 0;
            EndTransferTracking();
            _transfer?.Dispose();
            _transfer = null;
            SetBusy(false);
        }
    }

    /// <summary>
    /// Fetches the other machine's OWN session log — the plain-text record of who has connected to
    /// it and what moved — without the operator ever needing to know or type where it lives on that
    /// machine. Added 2026-09-03, after a real tester (who built this program) mistyped the path by
    /// hand on the first try: it is a hidden, username-specific folder, and asking anyone to type it
    /// from memory was always going to fail this exact way, for a tester and for a real client alike.
    ///
    /// <para>Uses the SAME reserved request (<see cref="FileGetRequest.SessionLogPath"/>) and the
    /// same consent already granted for looking at files — this is not a new permission, it is one
    /// fixed file reached without a path.</para>
    /// </summary>
    private async Task DownloadSessionLogAsync()
    {
        if (_busy) return;

        string? destination = _settings.LastDownloadFolder;
        if (destination is null || !Directory.Exists(destination))
        {
            using var pick = new FolderBrowserDialog
            {
                Description = "Where should their session log be saved? (Remembered for next time.)",
            };
            if (pick.ShowDialog(this) != DialogResult.OK) return;
            destination = pick.SelectedPath;
        }

        // Timestamped so fetching it again later in the same session — to see what has happened
        // since — never collides with the first copy.
        string saveAs = $"FlashDesk-session-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt";

        _transfer = new CancellationTokenSource();
        SetBusy(true);
        _progress.Value = 0;
        _status.Text = "Fetching their session log…";

        try
        {
            var end = await _files.DownloadAsync(
                FileGetRequest.SessionLogPath, destination, saveAs,
                Progress(0, "Fetching their session log…"), _transfer.Token);

            if (end.Status == FileStatus.Ok)
            {
                _settings.LastDownloadFolder = destination;
                _status.Text = $"Saved their session log ({Readable(end.TotalBytes)}) to {Path.Combine(destination, saveAs)}.";
            }
            else
            {
                _status.Text = end.Message.Length > 0 ? end.Message : "It did not finish.";
            }
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _progress.Value = 0;
            EndTransferTracking();
            _transfer?.Dispose();
            _transfer = null;
            SetBusy(false);
        }
    }

    /// <summary>
    /// Ctrl+C on a selected file: fetches it to a fresh temp folder and puts the real local file on
    /// the operator's Windows clipboard, so Ctrl+V works in any application — Explorer, an email,
    /// anywhere — with no special handling on their end, because it IS an ordinary local file by the
    /// time it lands there. Deliberately not a virtual/delay-rendered clipboard entry: those are
    /// unreliable across paste targets and would have to trigger consent and logging from inside a
    /// foreign process's UI thread, which is exactly the shape of bug this project spends most of
    /// its effort refusing to ship.
    /// </summary>
    private async Task CopySelectedToClipboardAsync()
    {
        if (_busy) return;
        var entry = Selected;
        if (entry is null || entry.IsDirectory || _folder.Length == 0)
        {
            _status.Text = "Choose a file first.";
            return;
        }

        if (entry.Size > ClipboardAskAboveBytes)
        {
            var confirm = MessageBox.Show(this,
                $"{entry.Name} is {Readable(entry.Size)}. Copying it now could take a while on a "
                + "slow connection.\n\nCopy it anyway?",
                "FlashDesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;
        }

        string tempFolder = Path.Combine(Path.GetTempPath(), "FlashDesk-clipboard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        _transfer = new CancellationTokenSource();
        SetBusy(true);
        _progress.Value = 0;
        _status.Text = "Copying…";

        try
        {
            var end = await _files.DownloadAsync(
                Path.Combine(_folder, entry.Name), tempFolder, entry.Name,
                Progress(entry.Size, "Copying…"), _transfer.Token);

            if (end.Status != FileStatus.Ok)
            {
                _status.Text = end.Message.Length > 0 ? end.Message : "It did not finish.";
                return;
            }

            var files = new StringCollection { Path.Combine(tempFolder, entry.Name) };
            Clipboard.SetFileDropList(files);
            _status.Text = $"Copied {entry.Name} ({Readable(end.TotalBytes)}) — paste it anywhere with Ctrl+V.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _progress.Value = 0;
            EndTransferTracking();
            _transfer?.Dispose();
            _transfer = null;
            SetBusy(false);
        }
    }

    /// <summary>
    /// Ctrl+V with real local files on the clipboard (from Explorer, or from another FlashDesk
    /// copy/paste): sends each one to the folder currently open here, exactly as "Send a file…"
    /// would — same consent, same per-program question, same log lines, one file at a time because
    /// only one transfer runs at once. Text on the clipboard is left alone entirely; see the type
    /// doc for why syncing clipboard TEXT is a different, unmade decision.
    /// </summary>
    private async Task PasteFilesAsync()
    {
        if (_busy) return;
        if (!Clipboard.ContainsFileDropList())
        {
            _status.Text = "There is no file on your clipboard to send.";
            return;
        }

        var paths = Clipboard.GetFileDropList().Cast<string>().Where(File.Exists).ToList();
        if (paths.Count == 0)
        {
            _status.Text = "There is no file on your clipboard to send.";
            return;
        }

        foreach (string path in paths)
        {
            if (_folder.Length == 0)
            {
                _status.Text = "Open a folder on their computer first — that is where it will go.";
                return;
            }
            await UploadFileAsync(path);
        }
    }

    private async Task UploadAsync()
    {
        if (_folder.Length == 0)
        {
            _status.Text = "Open a folder on their computer first — that is where it will go.";
            return;
        }

        using var open = new OpenFileDialog { Title = "Which file should be sent?", CheckFileExists = true };
        if (open.ShowDialog(this) != DialogResult.OK) return;

        await UploadFileAsync(open.FileName);
    }

    /// <summary>
    /// The actual send, wherever the local path came from — the "Send a file…" dialog, or a real
    /// local file pasted from the clipboard. Same consent, same per-program question, same log
    /// lines either way, because it is the exact same call underneath.
    /// </summary>
    private async Task UploadFileAsync(string localPath)
    {
        long size;
        try { size = new FileInfo(localPath).Length; }
        catch (Exception ex) { _status.Text = ex.Message; return; }

        _transfer = new CancellationTokenSource();
        SetBusy(true);
        _progress.Value = 0;
        _status.Text = "Waiting for them to answer — the question is on their screen now.";

        try
        {
            var (reply, result) = await _files.UploadAsync(localPath, _folder, Progress(size, "Sending…"), _transfer.Token);

            if (reply.Status != FileStatus.Ok)
            {
                _status.Text = reply.Message.Length > 0 ? reply.Message : "They did not accept it.";
                return;
            }

            // The name they agreed to may not be the name that was sent — they may have chosen
            // "keep both". Saying so is the whole reason that choice is worth offering.
            string landed = reply.SavedAs.Length > 0 ? reply.SavedAs : Path.GetFileName(localPath);
            _status.Text = result?.Status == FileStatus.Ok
                ? $"Sent. It was saved on their computer as {landed}, in {_folder}."
                : result?.Message.Length > 0 ? result.Value.Message : "It did not finish.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _progress.Value = 0;
            EndTransferTracking();
            _transfer?.Dispose();
            _transfer = null;
            SetBusy(false);
        }
    }

    /// <param name="activeStatusText">
    /// What to say the FIRST moment real bytes are confirmed moving. Needed because the status set
    /// before the call (e.g. upload's "Waiting for them to answer") is only true up to that point —
    /// left alone, it keeps saying "waiting" for the rest of the transfer even once the other side
    /// has answered and bytes are visibly flowing, which is its own version of a number answering
    /// the wrong question (found live, 2026-08-27: the technical view showed real KB/s while this
    /// label still claimed nobody had responded).
    /// </param>
    private IProgress<long> Progress(long total, string activeStatusText) => new Progress<long>(done =>
    {
        // Marked active on the FIRST real progress callback, not when the call is made — an upload
        // waits for the other side to accept first ("Waiting for them to answer"), and nothing is
        // slowing the picture while that wait has nothing to do with bytes moving. A download has
        // no such wait, so for a download this fires on the very first sample.
        if (!IsTransferActive)
        {
            IsTransferActive = true;
            _transferStartedAtMs = Environment.TickCount64;
            _status.Text = activeStatusText;
        }

        if (total <= 0) return;
        _progress.Value = (int)Math.Clamp(done * 1000 / total, 0, 1000);

        // Average rate from the transfer's OWN start, not an instantaneous reading — a single slow
        // or fast sample right after a burst would swing the estimate wildly. Needs at least a
        // second of real elapsed time before the rate means anything, same reasoning as the
        // MeasurableSendMs floor elsewhere in this project.
        long elapsedMs = Environment.TickCount64 - _transferStartedAtMs;
        if (elapsedMs < 1000 || done <= 0) { TransferTimeRemaining = null; return; }
        double bytesPerMs = done / (double)elapsedMs;
        long remainingBytes = total - done;
        TransferTimeRemaining = bytesPerMs > 0
            ? TimeSpan.FromMilliseconds(remainingBytes / bytesPerMs)
            : null;
    });

    private void EndTransferTracking()
    {
        IsTransferActive = false;
        TransferTimeRemaining = null;
    }

    // ---------------------------------------------------------------- plumbing

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _go.Enabled = _up.Enabled = _refresh.Enabled = _get.Enabled = _getSessionLog.Enabled = _send.Enabled = !busy;
        _more.Enabled = !busy && _hasMore;
        // Stop is the one control that is only useful WHILE something is happening, and it must
        // never be the harder button to reach when it is.
        _stop.Enabled = busy && _transfer is not null;
    }

    internal static string Readable(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB",
    };

    /// <summary>
    /// Catches Escape regardless of which child control currently holds focus (the list, the path
    /// box, a button) — a plain KeyDown handler on one control would miss the others. This is the
    /// standard WinForms way to intercept a key across a whole composite control before its children
    /// see it.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            RaiseEscapePressed();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// The logic half of the fix, split out so it is testable without a real window handle — see
    /// InputSuspendTests for the same reasoning applied to focus-dependent behaviour. ProcessCmdKey
    /// itself is not exercised by a test; it is the standard WinForms pattern and is verified by hand
    /// and by the two-machine test, same as the release order in InputCapture.Suspend.
    /// </summary>
    internal void RaiseEscapePressed() => EscapePressed?.Invoke();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _transfer?.Cancel();
        base.Dispose(disposing);
    }
}
