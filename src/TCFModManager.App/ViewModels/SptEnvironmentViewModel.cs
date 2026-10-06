using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime state for where SPT is installed and what version was detected there.
public partial class SptEnvironmentViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly SettingsService _settings = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallPathMissing))]
    private string? _installPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _installedVersion;

    [ObservableProperty]
    private string? _statusMessage;

    // True until an SPT install folder has been set.
    public bool IsInstallPathMissing => string.IsNullOrWhiteSpace(InstallPath);

    //
    // What the window and its title bar show - the app and its version, plus which SPT release
    // this window is pointed at.
    //
    // Running two installs side by side is normal, and every page, dialog and Data\ file belongs
    // to one of them. Until this was here the only way to tell two windows apart was to open the
    // Options page in each, which made a perfectly correct reading - the same mod at different
    // versions in each install - look like the app contradicting its own manifest.
    //
    // Deliberately the SPT version and nothing else. The install folder's name was tried first and
    // read as a client/server label, which is not what an install folder means. The cost is that
    // two installs on the same SPT line look alike; the title saying one unambiguous thing is
    // worth more than covering that case.
    //
    // Falls back to the bare app title when no version has been detected - no install set, or a
    // folder with no server exe in it - rather than showing an empty bracket.
    //
    public string WindowTitle => string.IsNullOrWhiteSpace(InstalledVersion)
        ? AppVersion.DisplayTitle
        : Text(Strings.SptEnvironment_TitleFormat, AppVersion.DisplayTitle, InstalledVersion);

    public SptEnvironmentViewModel()
    {
        var stored = _settings.Load().SptInstallPath;

        // Fork (1.3.0): unzipped into SPT\SSPTMM and started for the first time - that SPT install.
        if (string.IsNullOrWhiteSpace(stored) && SptRootResolver.InstallAroundApp(AppContext.BaseDirectory) is { } around)
        {
            AppLog.Info("InstallPath", $"no SPT install set; using the one SSPTMM sits in, {around}");
            SetInstallPath(around);
            return;
        }

        if (SptInstallationService.ToGameRoot(stored) != stored)
        {
            SetInstallPath(stored);
            return;
        }

        InstallPath = stored;
        Redetect();
    }

    public void SetInstallPath(string? path)
    {
        var gameRoot = SptInstallationService.ToGameRoot(path);
        if (gameRoot != path) AppLog.Info("InstallPath", $"{path} is the server folder; using the game root {gameRoot}");
        path = gameRoot;

        InstallPath = path;

        // Load-mutate-save rather than saving a fresh AppSettings: settings.json holds more than
        // the install path now (the skipped app-update version), and constructing a new object here
        // would silently drop everything this view model doesn't know about.
        var settings = _settings.Load();
        settings.SptInstallPath = path;
        _settings.Save(settings);

        Redetect();
    }

    private void Redetect()
    {
        var reading = SptInstallationService.GetInstalledVersion(InstallPath);

        InstalledVersion = reading.Version;

        StatusMessage = reading.Found
            ? Text(Strings.SptEnvironment_DetectedFormat, reading.Version)
            : Describe(reading);
    }

    //
    // Why the version could not be read. Core reports the reason and the values; the sentence is
    // here, which is the only place that shows it.
    //
    // NOT AppMessages.NoSptInstallFolder for the first case, deliberately: that one ends "configure
    // it on the Options page first", and this line is *on* the Options page, beside the button that
    // does it. The two saying different things is the point rather than an oversight.
    //
    private static string Describe(SptVersionReading reading) => reading.Problem switch
    {
        SptVersionProblem.NoInstallFolder => Strings.SptEnvironment_NoInstallFolder,

        SptVersionProblem.NoServerExe =>
            Text(Strings.SptEnvironment_NoServerExeFormat, reading.InstallPath),

        SptVersionProblem.NoVersionInExe =>
            Text(Strings.SptEnvironment_NoFileVersionFormat, reading.ExeName),

        SptVersionProblem.CouldNotReadExe =>
            Text(Strings.SptEnvironment_ReadFailedFormat, reading.ExeName, reading.Error?.Message),

        _ => Strings.SptEnvironment_Unknown,
    };
}
