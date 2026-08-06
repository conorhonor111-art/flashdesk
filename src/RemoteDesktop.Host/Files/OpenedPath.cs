using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RemoteDesktop.Host.Files;

/// <summary>
/// Asks Windows what an ALREADY-OPEN handle actually points at, and checks that against where it
/// was supposed to point.
///
/// <para><b>THIS IS THE ONLY THING BETWEEN THE PATH RULES AND ANOTHER MACHINE, and it is not
/// defence in depth.</b> Every check in <see cref="Shared.Files.RemotePath"/> works on a string.
/// A symbolic link or a junction is not visible in a string: <c>C:\Users\Ann\Documents</c> can BE
/// <c>\\fileserver\finance</c>, and <c>Path.GetFullPath</c> will happily agree it is on drive C.
/// So a path can pass every rule and still open a file on somebody else's server — one the person
/// in front of us cannot consent for.</para>
///
/// <para><b>And it must use the HANDLE, not the path.</b> Re-checking the path after opening would
/// leave the same race it is meant to close: a junction can be swapped between the check and the
/// open. <c>GetFinalPathNameByHandle</c> asks about the file that is open RIGHT NOW, so there is no
/// window to slip through.</para>
///
/// <para>The returned form is <c>\\?\C:\...</c> for a local file and <c>\\?\UNC\server\share\...</c>
/// for one on another machine — which is precisely the distinction that matters, and it is why the
/// UNC prefix is tested explicitly rather than inferred from a drive letter.</para>
/// </summary>
internal static class OpenedPath
{
    private const uint FILE_NAME_NORMALIZED = 0x0;
    private const uint VOLUME_NAME_DOS = 0x0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);

    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_ALL = 0x00000001 | 0x00000002 | 0x00000004; // read | write | delete
    private const uint OPEN_EXISTING = 3;

    /// <summary>Without this flag CreateFile refuses to open a FOLDER at all.</summary>
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    /// <summary>
    /// Opens a FOLDER just far enough to ask Windows where it really is.
    ///
    /// <para>.NET has no way to hold a directory handle — <c>FileStream</c> refuses one — so this is
    /// the P/Invoke it needs. Attributes only, shared with everything, so opening a folder to check
    /// it can never block anybody else's use of it.</para>
    /// </summary>
    internal static SafeFileHandle? OpenFolder(string path)
    {
        var handle = CreateFileW(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero,
            OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);

        if (handle.IsInvalid) { handle.Dispose(); return null; }
        return handle;
    }

    /// <summary>
    /// The real path of whatever this handle has open, or null if Windows would not say.
    /// </summary>
    internal static string? Of(SafeFileHandle handle)
    {
        // Ask for the length first: a path can exceed MAX_PATH, and guessing a buffer size here
        // would silently truncate the answer this whole check depends on.
        uint needed = GetFinalPathNameByHandleW(handle, Array.Empty<char>(), 0, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        if (needed == 0) return null;

        var buffer = new char[needed + 1];
        uint written = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        if (written == 0 || written >= buffer.Length) return null;

        return new string(buffer, 0, (int)written);
    }

    /// <summary>
    /// True when the open handle really is the file we meant to open, on this machine.
    /// </summary>
    /// <param name="handle">A handle that is already open. The check is worthless on a closed one.</param>
    /// <param name="expected">The canonical path the caller asked for.</param>
    /// <param name="problem">A sentence for the operator when it is not.</param>
    internal static bool IsWhereWeMeant(SafeFileHandle handle, string expected, out string? problem)
    {
        problem = null;

        string? actual = Of(handle);
        if (actual is null)
        {
            // Windows declining to answer is not proof of innocence. Refusing here costs a rare
            // legitimate transfer; allowing it would mean the one check that sees through a
            // junction can be switched off by making it fail.
            problem = "That file could not be verified and was not opened.";
            return false;
        }

        // \\?\UNC\server\share\... - the handle is on ANOTHER MACHINE. Very often an employer's
        // file server, which the person in front of us cannot consent for.
        if (actual.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            problem = "That location is on another computer on the network, not on this one.";
            return false;
        }

        // Strip the extended prefix Windows always returns, so the comparison is against the path
        // the caller actually asked for.
        string plain = actual.StartsWith(@"\\?\", StringComparison.Ordinal) ? actual[4..] : actual;

        if (!plain.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            // The handle is somewhere else than the name suggested: a link, a junction, or a
            // substitution between the check and the open.
            problem = "That name points somewhere else and was not opened.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// The same question for a destination folder: is the file we just created really inside the
    /// folder the operator chose, or did a junction move it?
    /// </summary>
    internal static bool IsInsideFolder(SafeFileHandle handle, string expectedFolder, out string? problem)
    {
        problem = null;

        string? actual = Of(handle);
        if (actual is null)
        {
            problem = "That destination could not be verified and nothing was written.";
            return false;
        }

        if (actual.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            problem = "That folder is on another computer on the network, not on this one.";
            return false;
        }

        string plain = actual.StartsWith(@"\\?\", StringComparison.Ordinal) ? actual[4..] : actual;
        string root = expectedFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

        if (!plain.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            problem = "That folder points somewhere else and nothing was written.";
            return false;
        }

        return true;
    }
}
