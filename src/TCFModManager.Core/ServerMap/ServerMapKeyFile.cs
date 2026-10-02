using System.Security.Cryptography;
using System.Text;

namespace TCFModManager.Core.ServerMap;

//
// Finds the shared key belonging to a Server Map server running on THIS machine.
//
// The point is the operator. They generated the key by running their own server, they own the file
// it is in, and asking them to go and find it in a folder to paste it back into the app one window
// away is busywork - so the app reads it for them. Nothing is disclosed by this: anyone who can run
// this app on that machine can already open the file.
//
// It is not a substitute for being given a key. On any machine that is not the server, no file is
// found and the box stays empty, which is correct - a key belongs to one server, and reading a
// local one to send to a remote server would just be a 401 with extra steps.
//
// Layouts are probed rather than mapped, the same way the server's own PayloadLoader finds its
// payload: from a starting folder, look for TCFModManager\ServerMap\config\ at that folder and each
// one above it. That covers the app sitting beside the install, inside it, and the 4.0 vs 4.1
// difference in where the server half lives, without this class knowing which is which.
//
public static class ServerMapKeyFile
{
    public const string FileName = "servermap-key.txt";

    //
    // The key this machine's own server expects, or null when this machine is not that server.
    //
    // Both starting points are tried because either can be the odd one out: the app usually lives in
    // <SPT root>\TCFModManager\, so its own folder finds the file immediately, but an install
    // managed from an app copied elsewhere only resolves from the configured SPT path.
    //
    public static string? TryReadLocal(string? sptInstallPath, string? appDirectory = null) =>
        TryFind(sptInstallPath, out var path, appDirectory) && TryRead(path, out var key) ? key : null;

    // The key file in this machine's Server Map config folder, if there is one.
    public static bool TryFind(string? sptInstallPath, out string path, string? appDirectory = null)
    {
        path = "";

        if (!ServerMapConfigFolder.TryFind(sptInstallPath, out var directory, appDirectory)) return false;

        var candidate = Path.Combine(directory, FileName);
        if (!File.Exists(candidate)) return false;

        path = candidate;
        return true;
    }

    //
    // Replaces this machine's key with a new one.
    //
    // The app writes the file and the server picks it up on its next request - it watches the file's
    // timestamp rather than caching for the life of the process - so rotating a key does not need
    // the server stopped. Everyone holding the old key stops working immediately, which is what
    // rotating a key is for.
    //
    // The alphabet and length are the server's, restated here rather than shared: this project does
    // not reference the server mod, and a key is 24 characters of a fixed alphabet in both places
    // because a person has to read it off one screen and type it into another.
    //
    public static bool TryRotateLocal(string? sptInstallPath, out string key, string? appDirectory = null)
    {
        key = "";

        //
        // Only where a key file already exists. Writing one into a folder that has none would create
        // a key for a server that is not installed, and the operator would hand out something no
        // server has ever heard of.
        //
        if (!TryFind(sptInstallPath, out var path, appDirectory)) return false;

        var generated = Generate();

        try
        {
            File.WriteAllText(path, generated + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        key = generated;
        return true;
    }

    // No I, L, O, U, 0 or 1 - every one of those is a transcription error waiting to happen when a
    // key is read off one screen and typed into another.
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int KeyLength = 24;

    private const int GroupSize = 4;

    public static string Generate()
    {
        var builder = new StringBuilder(KeyLength + KeyLength / GroupSize);

        for (var i = 0; i < KeyLength; i++)
        {
            if (i > 0 && i % GroupSize == 0) builder.Append('-');

            builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return builder.ToString();
    }

    //
    // The key as written. Trimmed but otherwise untouched - the server compares keys with dashes,
    // spaces and case normalised away, so there is nothing to be gained by reformatting it here and
    // something to be lost: what the app shows should be exactly what the operator sees in the file
    // and pastes to a friend.
    //
    public static bool TryRead(string path, out string key)
    {
        key = "";

        try
        {
            if (!File.Exists(path)) return false;

            var contents = File.ReadAllText(path).Trim();

            if (string.IsNullOrWhiteSpace(contents)) return false;

            key = contents;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
