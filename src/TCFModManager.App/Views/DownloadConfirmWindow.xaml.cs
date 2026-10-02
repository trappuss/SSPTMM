using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

// One download in the prompt: which mod and version, where it came from, and whether it is ticked.
public sealed partial class DownloadConfirmRow(DownloadedModRecord download) : ObservableObject
{
    public DownloadedModRecord Download { get; } = download;

    public string Name => Download.Name;

    public string Detail => LocalizationService.Text(
        Strings.DownloadConfirm_RowDetailFormat, Download.Version, Path.GetFileName(Download.ArchivePath));

    [ObservableProperty]
    private bool _isChecked = true;
}

//
// Asks whether the downloads a scan found installed should be recorded as installed. Every row
// starts ticked, since the scan has already checked each one's files on disk.
//
public partial class DownloadConfirmWindow : FluentWindow
{
    private readonly List<DownloadConfirmRow> _rows;

    private DownloadConfirmWindow(IReadOnlyList<DownloadedModRecord> downloads)
    {
        InitializeComponent();

        _rows = [.. downloads.Select(d => new DownloadConfirmRow(d))];

        SummaryText.Text = Strings.DownloadConfirm_Summary(downloads.Count);
        DownloadList.ItemsSource = _rows;

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
    }

    //
    // The ticked downloads, or null for "Not now" (and for closing the window, which says the same).
    // An empty set means the user answered and unticked everything.
    //
    public static IReadOnlySet<DownloadedModRecord>? Ask(IReadOnlyList<DownloadedModRecord> downloads)
    {
        var window = new DownloadConfirmWindow(downloads);
        return window.ShowDialog() == true
            ? window._rows.Where(r => r.IsChecked).Select(r => r.Download).ToHashSet()
            : null;
    }

    private void NotNowButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
