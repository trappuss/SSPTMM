using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>One finding on the Diagnose logs page, worded.</summary>
public sealed class LogFindingRow
{
    public LogFindingRow(LogFinding finding)
    {
        Finding = finding;
        (Title, Body) = Word(finding);

        var mods = finding.Mod is null ? [] : new[] { finding.Mod }.Concat(finding.Others).ToList();
        ModText = mods.Count switch
        {
            0 => null,
            1 => LocalizationService.Text(Strings.Diagnose_ModFormat, mods[0].DisplayName),
            _ => LocalizationService.Text(Strings.Diagnose_ModsFormat, TextLists.Join([.. mods.Select(m => m.DisplayName)])),
        };

        CountText = finding.Count > 1 ? LocalizationService.Text(Strings.Diagnose_CountFormat, finding.Count) : null;

        // The line itself and a few under it - enough to recognise, not the whole stack.
        var sample = finding.Sample;
        SampleText = finding.Quote ?? string.Join("\n", new[] { sample.Message }.Concat(sample.More.Take(4)));
        WhereText = LocalizationService.Text(Strings.Diagnose_LineFormat, Path.GetFileName(sample.File), sample.Line);
    }

    public LogFinding Finding { get; }

    public string Title { get; }

    public string Body { get; }

    public string? ModText { get; }

    public string? CountText { get; }

    public string SampleText { get; }

    public string WhereText { get; }

    public string Glyph => Finding.Severity switch
    {
        LogSeverity.Critical => "ErrorCircle24",
        LogSeverity.Warning => "Warning24",
        _ => "Info24",
    };


    public bool HasMod => Finding.Mod is not null;

    public bool IsAboutPort => Finding.Kind == LogFindingKind.PortInUse;

    public bool IsAboutProfile => Finding.Kind is LogFindingKind.ProfileInvalid or LogFindingKind.ProfileClothingMissing;

    private static string Arg(LogFinding f, int i) => i < f.Args.Count ? f.Args[i] : string.Empty;

    private static (string Title, string Body) Word(LogFinding f)
    {
        string T(string format, params object?[] values) => LocalizationService.Text(format, values);

        var fromMod = f.Mod is null ? string.Empty : " " + Strings.Diagnose_FromModCode;

        return f.Kind switch
        {
            LogFindingKind.ProfileInvalid => (Strings.Diagnose_Title_ProfileInvalid, T(Strings.Diagnose_Body_ProfileInvalid, Arg(f, 0))),
            LogFindingKind.ProfileClothingMissing => (Strings.Diagnose_Title_ProfileClothingMissing, T(Strings.Diagnose_Body_ProfileClothingMissing, Arg(f, 0))),
            LogFindingKind.RaidResultsLost => (Strings.Diagnose_Title_RaidResultsLost, T(Strings.Diagnose_Body_RaidResultsLost, Arg(f, 0))),
            LogFindingKind.PortInUse => (Strings.Diagnose_Title_PortInUse, T(Strings.Diagnose_Body_PortInUse, Arg(f, 0))),
            LogFindingKind.PluginMissingDependency => (Strings.Diagnose_Title_PluginMissingDependency, T(Strings.Diagnose_Body_PluginMissingDependency, Arg(f, 0), Arg(f, 1))),
            LogFindingKind.PluginIncompatible => (Strings.Diagnose_Title_PluginIncompatible, T(Strings.Diagnose_Body_PluginIncompatible, Arg(f, 0), Arg(f, 1))),
            LogFindingKind.PluginDependencyNotLoaded => (Strings.Diagnose_Title_PluginDependencyNotLoaded, T(Strings.Diagnose_Body_PluginDependencyNotLoaded, Arg(f, 0))),
            LogFindingKind.PluginDuplicate => (Strings.Diagnose_Title_PluginDuplicate, T(Strings.Diagnose_Body_PluginDuplicate, Arg(f, 0))),
            LogFindingKind.PluginLoadError => (Strings.Diagnose_Title_PluginLoadError, T(Strings.Diagnose_Body_PluginLoadError, Arg(f, 0), Arg(f, 1))),
            LogFindingKind.ServerModNotLoaded => (Strings.Diagnose_Title_ServerModNotLoaded, Strings.Diagnose_Body_ServerModNotLoaded),
            LogFindingKind.ModLoaderProblem => (Strings.Diagnose_Title_ModLoaderProblem, Arg(f, 0)),
            LogFindingKind.RequestFailed => (Strings.Diagnose_Title_RequestFailed, T(Strings.Diagnose_Body_RequestFailedFormat, Arg(f, 0), Arg(f, 1)) + fromMod),
            LogFindingKind.ScheduledTaskFailed => (Strings.Diagnose_Title_ScheduledTaskFailed, T(Strings.Diagnose_Body_ScheduledTaskFailedFormat, Arg(f, 0), Arg(f, 1)) + fromMod),
            LogFindingKind.ModReportedError => (Strings.Diagnose_Title_ModReportedError, Arg(f, 0)),
            LogFindingKind.BundleClash => (Strings.Diagnose_Title_BundleClash, T(Strings.Diagnose_Body_BundleClash, Arg(f, 0))),
            LogFindingKind.BundleMissingDependency => (Strings.Diagnose_Title_BundleMissingDependency, T(Strings.Diagnose_Body_BundleMissingDependency, Arg(f, 0))),
            _ => (Strings.Diagnose_Title_PluginLoggedErrors, Arg(f, 0)),
        };
    }
}

//
// Fork (SSPTMM): the Diagnose logs page (Tools) - LogDiagnoser over this install's logs, one card per
// finding, and a redacted report to paste where help is asked for. Read when the page is shown and on
// Read the logs again; nothing here changes any file.
//
public sealed partial class DiagnoseViewModel : LocalizedViewModel
{
    public ObservableCollection<LogFindingRow> Findings { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _serverLine;

    [ObservableProperty]
    private string? _gameLine;

    [ObservableProperty]
    private string? _gameErrorsLine;

    [ObservableProperty]
    private string? _launcherNote;

    [ObservableProperty]
    private string? _otherErrorsText;

    private LogDiagnosis? _last;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = Strings.Diagnose_NoInstall;
            return;
        }

        IsBusy = true;
        StatusMessage = Strings.Diagnose_Reading;
        try
        {
            var clock = Stopwatch.StartNew();
            _last = await Task.Run(() =>
            {
                var mods = LogModLocator.Build(InstalledModScanner.Scan(installPath));
                return LogDiagnoser.Diagnose(LogFiles.Find(installPath), mods);
            });

            AppLog.Info("Diagnose", $"{_last.Findings.Count} finding(s), {_last.OtherErrors} other error(s), in {clock.ElapsedMilliseconds}ms: "
                + string.Join("; ", _last.Findings.Select(f => $"{f.Kind} {f.Mod?.Name ?? "-"} x{f.Count}")));

            Show(_last);
        }
        catch (Exception ex)
        {
            // Never out of the page's Loaded handler: a log this page can't make sense of says so.
            AppLog.Warn("Diagnose", $"couldn't read the logs: {ex}");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(LogDiagnosis diagnosis)
    {
        Findings.Clear();
        foreach (var finding in diagnosis.Findings) Findings.Add(new LogFindingRow(finding));

        StatusMessage = diagnosis.Findings.Count == 0
            ? Strings.Diagnose_NothingFound
            : Strings.Diagnose_Found(diagnosis.Findings.Count, diagnosis.Findings.Count);

        var files = diagnosis.Files;
        ServerLine = files.Server is { } server
            ? LocalizationService.Text(Strings.Diagnose_ServerRunFormat, Path.GetFileName(server),
                diagnosis.ServerRunStartedAt?.ToString("g") ?? "?")
            : Strings.Diagnose_ServerLogNone;
        GameLine = files.BepInEx is null ? Strings.Diagnose_BepInExNone : Strings.Diagnose_BepInExFormat;
        GameErrorsLine = files.GameErrors is { } errors
            ? LocalizationService.Text(Strings.Diagnose_GameErrorsFormat, Path.GetFileName(Path.GetDirectoryName(errors)))
            : null;
        LauncherNote = files.LauncherClearsGameLogs ? Strings.Diagnose_LauncherClears : null;
        OtherErrorsText = diagnosis.OtherErrors > 0 ? Strings.Diagnose_OtherErrors(diagnosis.OtherErrors, diagnosis.OtherErrors) : null;
    }

    [RelayCommand]
    private void ShowMod(LogFindingRow? row)
    {
        if (row?.Finding.Mod is not { } mod) return;
        AppNavigation.ShowInSubscribedItems(mod.Name);
    }

    [RelayCommand]
    private void OpenModFolder(LogFindingRow? row)
    {
        if (row?.Finding.Mod is not { } mod) return;
        Start("explorer.exe", File.Exists(mod.FolderPath) ? $"/select,\"{mod.FolderPath}\"" : $"\"{mod.FolderPath}\"");
    }

    [RelayCommand]
    private void OpenLog(LogFindingRow? row)
    {
        if (row is null) return;
        Start("notepad.exe", $"\"{row.Finding.Sample.File}\"");
    }

    [RelayCommand]
    private void GoToPlay() => AppNavigation.Navigate(typeof(PlayPage));

    [RelayCommand]
    private void OpenProfileBackups() => AppNavigation.Navigate(typeof(OptionsPage));

    private void Start(string program, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (_last is null) return;

        try
        {
            Clipboard.SetText(BuildReport(_last));
            StatusMessage = Strings.Diagnose_Copied;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            StatusMessage = ex.Message;
        }
    }

    //
    // What was found and the lines behind it, then the errors no check knew - never whole logs, and
    // never a profile. Redacted last, over the whole text (LogRedactor).
    //
    private static string BuildReport(LogDiagnosis diagnosis)
    {
        string T(string format, params object?[] values) => LocalizationService.Text(format, values);

        var files = diagnosis.Files;
        var text = new StringBuilder();
        text.AppendLine(T(Strings.Diagnose_ReportHeader,
            SelfMod.ShortName + " " + AppVersion.Current, AppServices.SptEnvironment.InstalledVersion ?? "?"));
        text.AppendLine(T(Strings.Diagnose_ReportServerFormat,
            Path.GetFileName(files.Server) ?? "-", diagnosis.ServerRunStartedAt?.ToString("s") ?? "-"));
        text.AppendLine(T(Strings.Diagnose_ReportGameFormat,
            files.BepInEx is null ? "-" : Path.GetFileName(files.BepInEx),
            Path.GetFileName(Path.GetDirectoryName(files.GameErrors)) ?? "-"));
        text.AppendLine();

        foreach (var finding in diagnosis.Findings)
        {
            var row = new LogFindingRow(finding);
            text.Append('[').Append(finding.Severity).Append("] ").Append(row.Title);
            if (finding.Count > 1) text.Append(" (x").Append(finding.Count).Append(')');
            text.AppendLine();
            if (row.ModText is { } mod) text.Append("  ").AppendLine(mod);
            text.Append("  ").AppendLine(row.Body);
            text.Append("  ").Append(row.WhereText).AppendLine(":");
            foreach (var line in row.SampleText.Split('\n')) text.Append("    ").AppendLine(line);
            text.AppendLine();
        }

        if (diagnosis.Unexplained.Count > 0)
        {
            text.AppendLine(T(Strings.Diagnose_ReportOtherFormat, diagnosis.OtherErrors));
            foreach (var entry in diagnosis.Unexplained)
            {
                text.Append("  ").Append(Path.GetFileName(entry.File)).Append(':').Append(entry.Line)
                    .Append(" [").Append(entry.Level).Append("][").Append(entry.Source).Append("] ").AppendLine(entry.Message);
                foreach (var line in entry.More.Take(4)) text.Append("    ").AppendLine(line);
            }
        }

        var redacted = LogRedactor.Redact(
            text.ToString(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName);

        return redacted;
    }
}
