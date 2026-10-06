using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>One copy of the SPT profiles on the Options page.</summary>
public sealed class ProfileBackupRow(ProfileBackup backup)
{
    public ProfileBackup Backup { get; } = backup;

    public string When => Backup.TakenAt.ToString("g", System.Globalization.CultureInfo.CurrentCulture);

    public string Why => ProfileBackupsViewModel.Reason(Backup.Reason);

    public string Detail => LocalizationService.Text(Strings.Profiles_DetailFormat,
        Backup.Files, DownloadQueueItemViewModel.SizeLabel(Backup.Bytes));
}

//
// The copies of the SPT profiles taken before the app changed the install (see ProfileBackups):
// listed, taken by hand, put back.
//
public sealed partial class ProfileBackupsViewModel : LocalizedViewModel
{
    public ObservableCollection<ProfileBackupRow> Backups { get; } = [];

    public bool IsEmpty => Backups.Count == 0;

    public string KeepText => LocalizationService.Text(Strings.Profiles_DescriptionFormat, AppServices.ProfileBackups.Keep);

    // Fork (UI tidy-up 3): the one line on show in Options; KeepText is behind the "?" beside it.
    public string KeepSummary => LocalizationService.Text(Strings.Profiles_SummaryFormat, AppServices.ProfileBackups.Keep);

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackUpNowCommand), nameof(RestoreCommand))]
    private bool _isBusy;

    private static string? InstallPath => AppServices.SptEnvironment.InstallPath is { Length: > 0 } path ? path : null;

    public static string Reason(string reason) => reason switch
    {
        ProfileBackups.BeforeInstall => Strings.Profiles_ReasonInstall,
        ProfileBackups.BeforeRemove => Strings.Profiles_ReasonRemove,
        ProfileBackups.BeforeList => Strings.Profiles_ReasonList,
        ProfileBackups.BeforeDisable => Strings.Profiles_ReasonDisable,
        ProfileBackups.BeforePreset => Strings.Profiles_ReasonPreset,
        ProfileBackups.BeforeRestore => Strings.Profiles_ReasonRestore,
        ProfileBackups.BeforeWipe => Strings.Profiles_ReasonWipe,
        ProfileBackups.BeforeProfileDelete => Strings.Profiles_ReasonProfileDelete,
        _ => Strings.Profiles_ReasonManual,
    };

    public void Refresh()
    {
        Backups.Clear();
        if (InstallPath is { } install)
        {
            foreach (var backup in AppServices.ProfileBackups.List(install)) Backups.Add(new ProfileBackupRow(backup));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    // Fork (1.3.0, as TCF): each row's "why" and date are worded when it is made, so a language
    // change rebuilds them.
    protected internal override void RefreshText()
    {
        base.RefreshText();
        Refresh();
    }

    private bool CanChange => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task BackUpNowAsync()
    {
        if (InstallPath is not { } install)
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        IsBusy = true;
        try
        {
            var taken = await Task.Run(() => AppServices.ProfileBackups.BackupNow(install));
            StatusMessage = taken is null ? Strings.Profiles_NoneFound : Strings.Profiles_BackedUp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Error("Profiles", "backing up by hand failed", ex);
            StatusMessage = LocalizationService.Text(Strings.Profiles_FailedFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task RestoreAsync(ProfileBackupRow? row)
    {
        if (row is null) return;

        if (InstallPath is not { } install)
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var running = ModInstallService.RunningBlockers(install);
        if (running.Count > 0)
        {
            StatusMessage = ModInstallProblems.InstallInUse(running, ModInstallAction.RestoreProfiles);
            return;
        }

        if (MessageBox.Show(
                LocalizationService.Text(Strings.Profiles_RestoreConfirmFormat, row.When, row.Why),
                Strings.Profiles_RestoreTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await Task.Run(() => AppServices.ProfileBackups.Restore(row.Backup, install));
            StatusMessage = LocalizationService.Text(Strings.Profiles_RestoredFormat, row.When);
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Error("Profiles", "restoring a profile backup failed", ex);
            StatusMessage = LocalizationService.Text(Strings.Profiles_FailedFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (InstallPath is not { } install) return;

        var folder = AppServices.ProfileBackups.FolderFor(install);
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Profiles", $"couldn't open {folder}: {ex.Message}");
            StatusMessage = Strings.Common_FolderOpenFailed;
        }
    }
}
