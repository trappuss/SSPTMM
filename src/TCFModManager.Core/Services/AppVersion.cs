using System.Reflection;

namespace TCFModManager.Core.Services;

//
// The running app's own version, read from the assembly rather than typed anywhere in the UI.
//
// build/Directory.Build.props is the single place a release version is set; MSBuild turns
// &lt;Version&gt; into AssemblyInformationalVersion, which is the only form that keeps a pre-release
// suffix ("1.3.0-beta") - AssemblyVersion drops it and reports 1.3.0.0. The self-updater compares
// this against what sp-mod.com publishes, so a hardcoded string going stale here would mean the
// app either never offers an update or offers one it already has.
//
public static class AppVersion
{
    public static string Current { get; } = Resolve();

    // What the title bar shows.
    public static string DisplayTitle { get; } = $"{SelfMod.AppName} - {Current}";

    private static string Resolve()
    {
        // The entry assembly is the App exe; the fallback covers a test host, where the entry
        // assembly is the runner rather than anything of ours. Both projects take their version
        // from the same Directory.Build.props, so either answer is the same number.
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return assembly.GetName().Version?.ToString(3) ?? "unknown";

        // Deterministic builds append "+<commit sha>" as build metadata. It carries no precedence
        // and would only ever be noise in a title bar or a version comparison.
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
