using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// The hub's optional tabs and the window's background, as stored in the settings: what MainWindow
// binds to, and what the Options page changes. One shared instance, so a switch flicked in Options
// moves the tab and swaps the picture at that moment rather than at the next launch.
//
public sealed partial class AppearanceViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _showSubscribedItemsTab = true;

    [ObservableProperty]
    private bool _showCollectionsTab = true;

    // The user's picture, decoded once; null for the Steam grid.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackgroundImage))]
    private ImageSource? _backgroundImage;

    public bool HasBackgroundImage => BackgroundImage is not null;

    [ObservableProperty]
    private double _backgroundDarkness = 0.55;

    private string? _loadedPath;

    /// <summary>The folder the chosen picture is copied into.</summary>
    public static string BackgroundDirectory => Path.Combine(AppPaths.DataDirectory, "Background");

    // Re-reads the settings. Called at startup and whenever the Options page changes them.
    public void Refresh()
    {
        var settings = new SettingsService().Load();
        ShowSubscribedItemsTab = settings.ShowSubscribedItemsTab;
        ShowCollectionsTab = settings.ShowCollectionsTab;
        BackgroundDarkness = Math.Clamp(settings.BackgroundDarkness, 0, 0.9);

        if (settings.BackgroundImage != _loadedPath)
        {
            _loadedPath = settings.BackgroundImage;
            BackgroundImage = Load(settings.BackgroundImage);
        }
    }

    //
    // Decoded at no more than a large screen's width - a 6000px photo would otherwise hold a hundred
    // megabytes for the life of the window - and frozen. A picture that is gone or cannot be read
    // leaves the grid, and says so in the log.
    //
    private static ImageSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (!File.Exists(path))
            {
                AppLog.Info("Appearance", $"background picture {path} is gone; showing the grid");
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 2560;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException or InvalidOperationException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn("Appearance", $"background picture {path} could not be read: {ex.Message}");
            return null;
        }
    }
}
