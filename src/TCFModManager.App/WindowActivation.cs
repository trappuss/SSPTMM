using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TCFModManager.App;

//
// Bringing the main window to the front from outside it: a second launch of the exe (D11) or a
// click on an update notification. Both arrive while the user is somewhere else entirely, so the
// window may be minimised, behind other windows or - once the tray exists (§8a) - hidden.
//
internal static class WindowActivation
{
    public static void BringForward(Window window)
    {
        if (!window.IsVisible) window.Show();

        //
        // SW_RESTORE rather than WindowState = Normal: it puts a minimised window back the way it was
        // before it was minimised, maximised included. WPF's own property can only say Normal, which
        // would shrink a window that had been maximised.
        //
        if (window.WindowState == WindowState.Minimized)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero) ShowWindow(handle, SwRestore);
            else window.WindowState = WindowState.Normal;
        }

        window.Activate();

        //
        // Activate alone is refused by Windows when this process doesn't hold the foreground, and the
        // taskbar button flashes instead. A brief Topmost is the usual way past that for a window the
        // user has just asked for - a click on our own toast, or launching the exe again, which hands
        // its right to the foreground over first (SingleInstance).
        //
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
