namespace TCFModManager.Core.Services;

//
// Fork (1.4.7): copies of a profile that lives on another PC's server, kept here. Playing on a
// friend's server leaves nothing of the character on this PC; if that install is lost, so is the
// character. A Fika server hands a profile to its owner (SptLauncherApi.FikaProfileAsync), and the
// newest few are kept under Data\ProfileBackups\from-servers\<server>\.
//
// A copy is a profile file as SPT keeps them: put into an install's user\profiles, it is that
// character. Nothing here writes to a server.
//
public sealed class RemoteProfileCopies(string? root = null, int keep = 10)
{
    public string Root { get; } = root ?? Path.Combine(AppPaths.DataDirectory, "ProfileBackups", "from-servers");

    public int Keep { get; } = Math.Max(1, keep);

    /// <summary>Where one server's copies are kept: its address, as a folder name.</summary>
    public string FolderFor(string address) =>
        Path.Combine(Root, string.Concat(address.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_')));

    /// <summary>Keeps this profile, unless it is the same as the newest copy of it. The file written, or null.</summary>
    public string? Save(string address, string profileId, string json, DateTimeOffset now)
    {
        var folder = FolderFor(address);
        Directory.CreateDirectory(folder);

        var copies = Directory.GetFiles(folder, profileId + "_*.json").OrderByDescending(f => f, StringComparer.Ordinal).ToList();
        if (copies.Count > 0 && File.ReadAllText(copies[0]) == json) return null;

        var path = Path.Combine(folder, profileId + "_" + now.ToString("yyyyMMdd-HHmmss") + ".json");
        SafeFile.WriteText(path, json);

        foreach (var old in copies.Skip(Keep - 1))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("Profiles", $"couldn't drop an old profile copy: {ex.Message}");
            }
        }

        return path;
    }
}
