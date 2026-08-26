using RemoteDesktop.Shared.Files;
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
/// <para>Operator chrome, so it can never be mistaken for the client's window in a screenshot.</para>
/// </summary>
public sealed class FilePanel : UserControl
{
    private readonly ViewerFileClient _files;

    private readonly RoundedTextBox _path = new() { Font = Theme.Body, PlaceholderText = "C:\\Users" };
    private readonly Button _go = Theme.MakeButton("Go", ButtonKind.Neutral);
    private readonly Button _up = Theme.MakeButton("Up", ButtonKind.Neutral);
    private readonly Button _refresh = Theme.MakeButton("Refresh", ButtonKind.Neutral);
    private readonly ListView _list = new();
    private readonly Button _get = Theme.MakeButton("Copy to my computer", ButtonKind.Primary);
    private readonly Button _send = Theme.MakeButton("Send a file…", ButtonKind.Neutral);
    private readonly Button _more = Theme.MakeButton("Show more", ButtonKind.Neutral);
    private readonly Button _stop = Theme.MakeButton("Stop", ButtonKind.Destructive);
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Continuous, Height = 6, Maximum = 1000 };
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
        _list.Columns.Add("Name", 220);
        _list.Columns.Add("Size", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Changed", 100);
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; } };

        _go.Click += (_, _) => _ = ShowFolderAsync(_path.Text.Trim(), 0);
        _up.Click += (_, _) => GoUp();
        _refresh.Click += (_, _) => _ = ShowFolderAsync(_folder, 0);
        _more.Click += (_, _) => _ = ShowFolderAsync(_folder, _shown);
        _get.Click += (_, _) => _ = DownloadSelectedAsync();
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
        foreach (Control c in new Control[] { _get, _send, _more, _stop })
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
        if (entry is null || !entry.IsDirectory) return;

        // At the root the NAME IS THE PATH (the drives), so it is used as-is rather than joined to
        // an empty folder — see LocalDrives.Roots.
        string next = _folder.Length == 0 ? entry.Name : Path.Combine(_folder, entry.Name);
        _ = ShowFolderAsync(next, 0);
    }

    private void GoUp()
    {
        if (_folder.Length == 0) return;

        string? parent = Path.GetDirectoryName(_folder.TrimEnd(Path.DirectorySeparatorChar));
        // No parent means we were at a drive root, so up goes to the list of drives.
        _ = ShowFolderAsync(string.IsNullOrEmpty(parent) ? string.Empty : parent, 0);
    }

    // ---------------------------------------------------------------- moving files

    private async Task DownloadSelectedAsync()
    {
        var entry = Selected;
        if (entry is null || entry.IsDirectory || _folder.Length == 0)
        {
            _status.Text = "Choose a file first.";
            return;
        }

        using var save = new SaveFileDialog
        {
            FileName = entry.Name,
            Title = "Where should this be saved on your computer?",
            OverwritePrompt = true,
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;

        string destination = Path.GetDirectoryName(save.FileName) ?? "";
        if (destination.Length == 0) { _status.Text = "That is not a folder."; return; }

        _transfer = new CancellationTokenSource();
        SetBusy(true);
        _progress.Value = 0;

        try
        {
            var end = await _files.DownloadAsync(
                Path.Combine(_folder, entry.Name), destination, Path.GetFileName(save.FileName),
                Progress(entry.Size), _transfer.Token);

            _status.Text = end.Status == FileStatus.Ok
                ? $"Copied {entry.Name} ({Readable(end.TotalBytes)}) to {destination}."
                : end.Message.Length > 0 ? end.Message : "It did not finish.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _progress.Value = 0;
            _transfer?.Dispose();
            _transfer = null;
            SetBusy(false);
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

        long size;
        try { size = new FileInfo(open.FileName).Length; }
        catch (Exception ex) { _status.Text = ex.Message; return; }

        _transfer = new CancellationTokenSource();
        SetBusy(true);
        _progress.Value = 0;
        _status.Text = "Waiting for them to answer — the question is on their screen now.";

        try
        {
            var (reply, result) = await _files.UploadAsync(open.FileName, _folder, Progress(size), _transfer.Token);

            if (reply.Status != FileStatus.Ok)
            {
                _status.Text = reply.Message.Length > 0 ? reply.Message : "They did not accept it.";
                return;
            }

            // The name they agreed to may not be the name that was sent — they may have chosen
            // "keep both". Saying so is the whole reason that choice is worth offering.
            string landed = reply.SavedAs.Length > 0 ? reply.SavedAs : Path.GetFileName(open.FileName);
            _status.Text = result?.Status == FileStatus.Ok
                ? $"Sent. It was saved on their computer as {landed}, in {_folder}."
                : result?.Message.Length > 0 ? result.Value.Message : "It did not finish.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _progress.Value = 0;
            _transfer?.Dispose();
            _transfer = null;
            SetBusy(false);
        }
    }

    private IProgress<long> Progress(long total) => new Progress<long>(done =>
    {
        if (total <= 0) return;
        _progress.Value = (int)Math.Clamp(done * 1000 / total, 0, 1000);
    });

    // ---------------------------------------------------------------- plumbing

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _go.Enabled = _up.Enabled = _refresh.Enabled = _get.Enabled = _send.Enabled = !busy;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _transfer?.Cancel();
        base.Dispose(disposing);
    }
}
