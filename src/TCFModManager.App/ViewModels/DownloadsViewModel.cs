using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.SpModApi;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class DownloadsViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly SpModApiClient _spModApi;
    private readonly ModDownloadService _downloadService;

    public DownloadsViewModel() : this(AppServices.SpModApi, AppServices.Downloads)
    {
    }

    public DownloadsViewModel(SpModApiClient spModApi, ModDownloadService downloadService)
    {
        _spModApi = spModApi;
        _downloadService = downloadService;
    }

    [ObservableProperty]
    private string _modId = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    // Defaults to AppPaths.StagingDirectory, a "Staging" folder next to the exe.
    [ObservableProperty]
    private string _destinationFolder = AppPaths.StagingDirectory;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(ModId) || string.IsNullOrWhiteSpace(Version))
        {
            StatusMessage = Strings.Downloads_EnterModId;
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusMessage = null;
        try
        {
            var versions = await _spModApi.GetModVersionsAsync(
                ModId.Trim(),
                new ModVersionsQuery { FilterVersion = Version.Trim(), PerPage = 5 });

            var match = versions.Data.Exactly(Version);
            if (match?.Link is null)
            {
                StatusMessage = Strings.Downloads_VersionNotFound;
                return;
            }

            var fileName = $"{ModId.Trim()}-{match.Version}.zip";
            var destination = Path.Combine(DestinationFolder, fileName);
            var progress = new Progress<double>(p => Progress = p);

            await _downloadService.DownloadAsync(match.Link, destination, progress);

            StatusMessage = Text(Strings.Downloads_DownloadedToFormat, destination);
        }
        catch (SpModApiRateLimitedException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (SpModApiException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = Text(Strings.Downloads_FailedFormat, ex.Message);
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (IOException ex)
        {
            StatusMessage = Text(Strings.Downloads_WriteFailedFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
