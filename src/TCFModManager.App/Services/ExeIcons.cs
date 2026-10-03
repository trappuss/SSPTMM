using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TCFModManager.App.Services;

//
// Fork (SSPTMM): an .exe's own icon, as Explorer shows it, for the Play page's mod tools. Asked of the
// Windows shell (SHGetFileInfo) rather than read from the file, so it is exactly the icon the user
// knows the tool by. Null when the shell has none to give; the row then shows a plain tool symbol.
//
public static class ExeIcons
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    public static ImageSource? For(string exePath)
    {
        try
        {
            var info = new ShFileInfo();
            if (SHGetFileInfo(exePath, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon) == IntPtr.Zero
                || info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException or ArgumentException)
        {
            return null;
        }
    }
}
