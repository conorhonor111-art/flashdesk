using RemoteDesktop.Shared.Files;

namespace RemoteDesktop.Host.Files;

/// <summary>
/// Which drives of this machine may be looked at — the SECOND half of the path rule, and it has to
/// run here rather than in <see cref="RemotePath"/>.
///
/// <para><see cref="RemotePath"/> decides everything a string can decide: shape, canonical form, and
/// whether the path names something off this machine outright. What it deliberately cannot decide is
/// whether <c>Z:\</c> is a real disk in this computer or a mapped folder on an employer's file
/// server — that needs <c>DriveInfo</c>, which only means anything on the machine actually running
/// the host. Its comment says both checks must pass; this is the other one.</para>
///
/// <para><b>A MAPPED NETWORK DRIVE IS ANOTHER MACHINE WEARING A DRIVE LETTER.</b> That is the whole
/// reason this file exists. <c>\\fileserver\finance</c> is refused by the string rules and everyone
/// can see why; the same share mapped to <c>Z:</c> passes every one of them, because by then it is
/// spelled like an ordinary local path. The person sitting in front of us cannot consent on behalf
/// of their employer, so a drive letter is not enough — the drive has to be a disk.</para>
///
/// <para>A junction or a symbolic link INSIDE an allowed drive can still lead off the machine, and
/// nothing here can see that. <see cref="OpenedPath"/> is what catches it, after the handle is
/// open.</para>
/// </summary>
internal static class LocalDrives
{
    /// <summary>
    /// True for a drive that is genuinely part of this computer. Network is the one that matters;
    /// the rest are listed explicitly rather than by exclusion, so a drive type Windows invents
    /// later is refused by default instead of being allowed by an oversight.
    /// </summary>
    private static bool IsLocal(DriveType type) =>
        type is DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram;

    /// <summary>
    /// The drives to show at the root of the browser. Ready drives only: an empty card reader
    /// answers questions about itself very slowly, and a row that hangs when clicked is worse than
    /// a row that is not there.
    /// </summary>
    internal static List<DirEntry> Roots()
    {
        var entries = new List<DirEntry>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return entries; }

        foreach (var drive in drives)
        {
            try
            {
                if (!IsLocal(drive.DriveType) || !drive.IsReady) continue;
                // The NAME IS THE PATH, deliberately: the panel navigates by joining what it was
                // given, so a friendlier "C: (Windows)" here would become a folder that cannot be
                // opened. The label belongs in the viewer's own column, not in the address.
                entries.Add(new DirEntry(drive.RootDirectory.FullName, 0, true, 0));
            }
            catch
            {
                // A drive that throws while being asked what it is does not get listed.
            }
        }
        return entries;
    }

    /// <summary>
    /// True when a canonical path sits on a drive of this machine. <paramref name="problem"/> is a
    /// sentence for the operator; it never blames them, because the usual cause is an ordinary
    /// mapped drive rather than anything they did wrong.
    /// </summary>
    internal static bool IsOnLocalDrive(string canonicalPath, out string? problem)
    {
        problem = null;

        string? root;
        try { root = Path.GetPathRoot(canonicalPath); }
        catch { root = null; }

        if (string.IsNullOrEmpty(root))
        {
            problem = "That location is not on a drive of this computer.";
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            if (!IsLocal(drive.DriveType))
            {
                problem = "That drive is a folder on another computer on the network, not a disk in this one.";
                return false;
            }

            if (!drive.IsReady)
            {
                problem = "That drive is not ready — there may be no disk in it.";
                return false;
            }
        }
        catch
        {
            problem = "That drive could not be checked, so it was not opened.";
            return false;
        }

        return true;
    }
}
