namespace TCFModManager.Core.Services;

//
// The files in an SPT install that belong to SPT, BepInEx or the game rather than to any mod: never
// placed over, never recorded, never removed. One rule set, read by the installer, by Monitor mode's
// archive plan and by every removal path (CLOSED-10-TCFResilience-DESIGN.md §3, D1/D2).
//
// It works on the install-relative path alone and needs no server root: both server layouts (SPT\
// on 4.0, SPT_Runtime\ on 4.1) and a standalone server at the install root are all recognised by
// shape, so a detection that failed or picked the wrong folder can never leave SPT unprotected.
//
// A path it cannot read safely - rooted, carrying a drive or "..", or empty - counts as protected.
// Anything asking this question is about to write or delete, and "could not tell" must mean "no".
//
public static class ProtectedInstallPaths
{
    private static readonly string[] ServerRootNames = ["SPT", "SPT_Runtime"];

    // The only folders under <server>/user that hold mods; everything else there is SPT's or the
    // player's (profiles, certs, credentials, settings, registry, app data).
    private static readonly string[] UserModFolders = ["mods", "mods" + DisabledModPaths.DisabledSuffix];

    private static readonly string[] SptPatchers = ["spt-prepatch.dll", "aki-prepatch.dll"];

    public static bool IsProtected(string? installRelative)
    {
        if (Segments(installRelative) is not { } s) return true;

        // Any file sitting directly in the install root: the game, the loader, UnityPlayer.dll, and
        // on a standalone server the server's own files (R3).
        if (s.Length == 1) return true;

        if (Is(s[0], "MonoBleedingEdge") || Is(s[0], "NLog")) return true;
        if (Is(s[0], "EscapeFromTarkov_Data") && Is(s[1], "Managed")) return true;

        if (Is(s[0], "BepInEx")) return IsProtectedInBepInEx(s);

        if (ServerRootNames.Any(name => Is(s[0], name)))
        {
            var rest = s[1..];

            // Any file directly in the server folder: SPT.Server.exe, SPT.Launcher.exe, SPTarkov.*.dll
            // and the libraries beside them (R3).
            if (rest.Length == 1) return true;

            return IsProtectedInServer(rest);
        }

        // A standalone server keeps SPT_Data and user at the install root.
        return IsProtectedInServer(s);
    }

    //
    // The two protected areas where a mod may still ADD a file (R17, found by the stage-6 proof run):
    // a loose file directly in the install root (SVM's Greed.exe) and anything under
    // EscapeFromTarkov_Data/Managed (Dynamic Maps' Unity.VectorGraphics.dll). IsProtected stays true
    // for them, so every caller that doesn't ask this is as strict as before. The few that do must
    // prove the rest themselves: the installer places there only where nothing exists yet, a removal
    // takes from there only what its fingerprint proves this app placed, and Undo puts back only into
    // a free path.
    //
    public static bool IsNewFileOnly(string? installRelative)
    {
        if (Segments(installRelative) is not { } s) return false;

        if (s.Length == 1) return true;

        return s.Length >= 3 && Is(s[0], "EscapeFromTarkov_Data") && Is(s[1], "Managed");
    }

    private static bool IsProtectedInBepInEx(string[] s)
    {
        if (Is(s[1], "core")) return true;

        if (s.Length >= 3 && Is(s[1], "plugins") && Is(s[2], "spt")) return true;

        if (s.Length == 3 && Is(s[1], "patchers") && SptPatchers.Any(p => Is(s[2], p))) return true;

        return s.Length == 3 && Is(s[1], "config") && Is(s[2], "BepInEx.cfg");
    }

    // s starts at the server root's first segment below it.
    private static bool IsProtectedInServer(string[] s)
    {
        if (s.Length == 0) return true;

        if (Is(s[0], "SPT_Data")) return true;

        if (Is(s[0], "user"))
        {
            // user/mods/<anything> is mod territory; user itself, user/mods itself, and everything
            // else under user is not.
            return !(s.Length >= 3 && UserModFolders.Any(f => Is(s[1], f)));
        }

        return false;
    }

    //
    // The path as its segments, or null when it isn't a plain install-relative path. Accepts either
    // separator and a leading "./".
    //
    internal static string[]? Segments(string? installRelative)
    {
        if (string.IsNullOrWhiteSpace(installRelative)) return null;

        var path = installRelative.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];

        if (path.StartsWith('/') || path.Contains(':')) return null;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToArray();

        if (segments.Length == 0 || segments.Any(segment => segment == "..")) return null;

        return segments;
    }

    private static bool Is(string segment, string name) =>
        string.Equals(segment, name, StringComparison.OrdinalIgnoreCase);
}
