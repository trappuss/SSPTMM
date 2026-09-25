using System.Runtime.InteropServices;

namespace TCFModManager.App.Behaviors;

//
// Which pictures are safe to hand to WPF's decoder on this machine.
//
// Under Wine (how SPT is played on Linux), WPF's GIF decoding does not fail with an exception the
// app can catch: it crashes the whole process (an access violation in the decoder's COM release).
// Reproduced with Wine 9 on every GIF tried, from a 37x21 still to a 1052x746 animation, on the
// main thread of a bare WPF program, so it is Wine's imaging and not anything this app does. A
// WebP it cannot read is refused cleanly there, as on Windows, so only GIFs are held back - and
// only under Wine. Animated GIFs in descriptions never go this way at all: XamlAnimatedGif reads
// those itself (see RemotePicture).
//
internal static class ImageSupport
{
    private static readonly Lazy<bool> RunningUnderWine = new(() =>
    {
        try
        {
            var ntdll = GetModuleHandle("ntdll.dll");
            return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    });

    /// <summary>False for bytes this machine's decoder cannot be trusted with.</summary>
    public static bool CanDecode(ReadOnlySpan<byte> bytes) => !(RunningUnderWine.Value && IsGif(bytes));

    private static bool IsGif(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8';

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
}
