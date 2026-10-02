using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TCFModManager.Core.Services;

namespace TCFModManager.App;

//
// One running copy per app folder (D11, docs\CLOSED-07-TCFUpdateNotifications-DESIGN.md §8a).
//
// Two copies of the same app writing the same Data\ folder would each save over the other's
// settings, manifest and caches. Keyed on the exe's folder rather than on the app, so a second copy
// of the app kept beside a second SPT install - the usual way to run one per install - still starts.
//
// A second launch doesn't open anything: it tells the running copy to bring its window forward and
// exits. That is also what makes a copy hidden in the tray (step 5) reachable from a shortcut.
//
internal static class SingleInstance
{
    // Held for the life of the process. Windows releases it when the process ends, however it ends.
    private static Mutex? _mutex;

    private static EventWaitHandle? _showSignal;

    private static RegisteredWaitHandle? _showWait;

    //
    // True for the first copy, which goes on to start normally and calls onShowRequested (on a
    // thread-pool thread) each time another launch asks for the window. False for any later copy,
    // which has already signalled the first and should exit without opening anything.
    //
    // Fails open: if the mutex can't be made at all, the app starts rather than refusing to.
    //
    public static bool Claim(Action onShowRequested)
    {
        var key = KeyFor(AppContext.BaseDirectory);

        try
        {
            _mutex = new Mutex(initiallyOwned: true, $@"Local\TCFModManager-{key}", out var createdNew);
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\TCFModManager-{key}-show");

            if (!createdNew)
            {
                // The copy starting now was launched by the user, so it holds the right to take the
                // foreground. Handing that on is what lets the running copy's window actually come
                // to the front instead of flashing on the taskbar.
                AllowSetForegroundWindow(AsfwAny);
                _showSignal.Set();

                AppLog.Info("App", "already running from this folder - asked it to show its window and exiting");
                return false;
            }

            _showWait = ThreadPool.RegisterWaitForSingleObject(
                _showSignal, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            AppLog.Warn("App", $"couldn't check for another running copy, starting anyway: {ex.Message}");
            return true;
        }
    }

    // The folder, normalised and hashed, so the name is short and has no path characters in it.
    internal static string KeyFor(string folder)
    {
        var normalised = Path.GetFullPath(folder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)))[..16];
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
