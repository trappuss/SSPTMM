using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (1.4.7): Add from GitHub, on the "..." menu beside Install from file - a mod installed from a
// repository's newest release, through the download queue like any other install, and remembered
// (GitHubMods) so its card says when a newer release is out and Update installs it.
//
public partial class InstalledViewModel
{
    // HUNCH: as many of a release's archives as fit as buttons on one dialog.
    private const int GitHubFileChoices = 4;

    [RelayCommand]
    private async Task AddFromGitHubAsync()
    {
        if (AppServices.SptEnvironment.InstallPath is not { Length: > 0 } installPath)
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        if (AskRepository() is not { } text) return;

        if (GitHubMods.RepositoryFrom(text) is not { } repository)
        {
            StatusMessage = Text(Strings.GitHub_NotARepositoryFormat, text);
            return;
        }

        StatusMessage = Text(Strings.GitHub_ReadingFormat, repository);

        List<GitHubMods.Release> releases;
        string? etag;
        try
        {
            (releases, etag) = await AppServices.GitHubMods.ReleasesAsync(repository);
        }
        catch (GitHubReleaseCheckException ex)
        {
            StatusMessage = ex.RateLimited
                ? Strings.GitHub_RateLimited
                : ex.Status == HttpStatusCode.NotFound
                ? Text(Strings.GitHub_NotFoundFormat, repository)
                : Text(Strings.GitHub_UnreadableFormat, repository, ex.Message);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            AppLog.Warn("GitHub", $"couldn't read {repository}'s releases: {ex.Message}");
            StatusMessage = Text(Strings.GitHub_UnreadableFormat, repository, ex.Message);
            return;
        }

        // The newest release with a file to install - source code alone is not a mod to install.
        if (releases.FirstOrDefault(r => GitHubMods.ArchivesOf(r).Count > 0) is not { } release)
        {
            StatusMessage = releases.Count == 0
                ? Text(Strings.GitHub_NoReleasesFormat, repository)
                : Text(Strings.GitHub_NoArchiveFormat, repository);
            return;
        }

        if (ChooseFile(repository, release) is not { } file)
        {
            StatusMessage = null;
            return;
        }

        var name = repository.Split('/')[1];
        if (SteamMessageBox.Show(
                Text(Strings.GitHub_ConfirmFormat, name, release.Version, file.Name, repository),
                Strings.GitHub_AddTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            StatusMessage = null;
            return;
        }

        var entry = AppServices.GitHubMods.Remember(repository, releases, etag, file);
        var version = release.Version;

        AppServices.DownloadQueue.Enqueue(
            new InstallTarget(entry.Id, false, name, null, null, GitHubMods.ReleasesPageOf(repository)),
            version,
            installPath,
            () => Task.FromResult(AppServices.GitHubMods.VersionFor(entry.Id, version)),
            checkDependencies: false, // GitHub says nothing about what a mod needs
            totalBytes: file.Size);

        StatusMessage = Text(Strings.Installed_LocalQueuedFormat, name);
    }

    // The repository's address as typed or pasted, or null when the dialog was cancelled.
    private static string? AskRepository()
    {
        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource("SteamSearchField"),
            Tag = Strings.GitHub_AddHint,
            Height = 34,
            MinWidth = 480,
        };
        box.Loaded += (_, _) => box.Focus();

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = Strings.GitHub_AddBody,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            Margin = new Thickness(0, 0, 0, 12),
        });
        body.Children.Add(box);

        var answer = SteamDialog.Show(
            Strings.GitHub_AddTitle,
            body,
            new SteamDialogChoice(Strings.GitHub_AddButton, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        return answer == 0 && box.Text.Trim() is { Length: > 0 } text ? text : null;
    }

    // The release's file to install: its only archive, or the one picked when it has several.
    private static GitHubMods.Asset? ChooseFile(string repository, GitHubMods.Release release)
    {
        var archives = GitHubMods.ArchivesOf(release);
        if (archives.Count == 1) return archives[0];

        var offered = archives.Take(GitHubFileChoices).ToList();
        var choices = offered
            .Select(a => new SteamDialogChoice(a.Name, SteamDialogButton.Blue))
            .Append(new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey))
            .ToArray();

        var answer = SteamDialog.Show(
            Strings.GitHub_ChooseFileTitle,
            Text(Strings.GitHub_ChooseFileFormat, repository, release.Version),
            choices);

        return answer >= 0 && answer < offered.Count ? offered[answer] : null;
    }
}
