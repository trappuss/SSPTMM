using System.Runtime.InteropServices;

namespace TCFModManager.Core.Services;

//
// Where Monitor mode saves archives, and what it calls them.
//
public static class DownloadFolders
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    // Windows' own invalid set, spelled out so a name built on another OS is still valid on Windows.
    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // The folder a setting names, or the Windows Downloads folder when it names none.
    public static string Resolve(string? setting) =>
        string.IsNullOrWhiteSpace(setting) ? Default() : setting.Trim();

    //
    // The user's Downloads folder. Asked of Windows rather than assumed, since it can be moved to
    // another drive from its Properties page; %USERPROFILE%\Downloads is only the fallback.
    //
    public static string Default()
    {
        if (OperatingSystem.IsWindows() && TryKnownFolder(DownloadsFolderId) is { } known) return known;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    //
    // A name safe to use as one file or folder name on Windows: invalid and control characters become
    // "_", trailing dots and spaces go (Windows drops them silently, so two names would collide), and
    // a reserved device name is prefixed. Never empty.
    //
    public static string SafeName(string? name, string fallback = "download")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        var chars = name.Trim()
            .Select(c => char.IsControl(c) || InvalidNameChars.Contains(c) ? '_' : c)
            .ToArray();
        var safe = new string(chars).TrimEnd('.', ' ');

        if (safe.Length == 0) return fallback;

        var stem = safe.Split('.', 2)[0];
        return ReservedNames.Contains(stem) ? "_" + safe : safe;
    }

    //
    // What a saved archive is called: the server's own name when it offered one, otherwise
    // "<mod>-<version>". An offered name without an extension gets the one its header says it is.
    //
    public static string FileNameFor(string? offeredName, string modName, string? version, ArchiveKind kind)
    {
        if (!string.IsNullOrWhiteSpace(offeredName))
        {
            var offered = SafeName(offeredName);
            return Path.HasExtension(offered) ? offered : offered + ArchiveLayout.ExtensionFor(kind);
        }

        var stem = string.IsNullOrWhiteSpace(version) ? modName : $"{modName}-{version}";
        return SafeName(stem) + ArchiveLayout.ExtensionFor(kind);
    }

    //
    // <paramref name="fileName"/> in <paramref name="folder"/>, or "name (2).ext", "name (3).ext"...
    // when it is taken - the way Explorer does it. Nothing is ever overwritten.
    //
    public static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path) && !Directory.Exists(path)) return path;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var n = 2; ; n++)
        {
            path = Path.Combine(folder, $"{stem} ({n}){extension}");
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }

    private static string? TryKnownFolder(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var pointer) != 0) return null;

            try
            {
                var path = Marshal.PtrToStringUni(pointer);
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
