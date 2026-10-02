using TCFModManager.Core.Services;

namespace TCFModManager.Core.SpModApi;

public sealed class SpModApiOptions
{
    public string BaseUrl { get; init; } = "https://sp-mod.com";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    // Sent as the User-Agent header. Built from the running assembly's version rather than typed
    // here, which is what stops it going stale - it read "TCFModManager/0.1" while releases were at
    // 1.3.0-beta. Keeping it derived also means the release script only ever has one version to
    // write (build\Directory.Build.props), instead of rewriting a source file too.
    public string UserAgent { get; init; } = $"{SelfMod.ShortName}/{AppVersion.Current}";
}
