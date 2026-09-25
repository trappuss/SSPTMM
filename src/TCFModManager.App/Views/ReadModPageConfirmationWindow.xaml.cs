using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

// One row in ReadModPageConfirmationWindow - a mod name plus the button that opens its sp-mod.com page.
public sealed class ModPageLink(string name, string? url) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public string? Url { get; } = url;
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    private bool _isOpened = string.IsNullOrWhiteSpace(url);
    public bool IsOpened
    {
        get => _isOpened;
        set
        {
            if (_isOpened == value) return;
            _isOpened = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpened)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonLabel)));
        }
    }

    public string ButtonLabel => !HasUrl
        ? Strings.ReadModPage_ButtonNoPage
        : IsOpened ? Strings.ReadModPage_ButtonOpened : Strings.ReadModPage_ButtonOpen;

    public event PropertyChangedEventHandler? PropertyChanged;
}

// 
// Modal gate shown before mods are queued for install/update, requiring every listed mod's page
// to be opened before the Continue button unlocks. Implemented as a plain FluentWindow so it can
// be shown via ShowDialog() from any call site.
// 
public partial class ReadModPageConfirmationWindow : FluentWindow
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly List<ModPageLink> _links;

    //
    // Above this many pages the window stops expecting one click per mod and offers to open them a
    // batch at a time, or not at all. A multi-select update or a mod list can list forty mods, and
    // forty browser tabs opened back to back is enough to leave the machine unusable for a while -
    // long enough to read as the app having hung.
    //
    private const int BatchThreshold = 5;
    private const int BatchSize = 5;

    // Set by the Skip button, which only exists in batch mode. Unlocks Continue without every page
    // having been opened.
    private bool _openingSkipped;

    // Single-mod gate - used by a direct Install/Update click.
    public ReadModPageConfirmationWindow(string modName, string? modPageUrl)
        : this([new ModPageLink(modName, modPageUrl)])
    {
    }

    // Multi-mod gate - used for a batch of missing dependencies.
    public ReadModPageConfirmationWindow(IReadOnlyList<ModPageLink> links)
    {
        _links = links.ToList();
        InitializeComponent();

        WindowTitleBar.Title = Title = _links.Count == 1
            ? Text(Strings.ReadModPage_TitleNamedFormat, _links[0].Name)
            : Text(Strings.ReadModPage_TitleCountFormat, _links.Count);

        LinksList.ItemsSource = _links;
        foreach (var link in _links) link.PropertyChanged += (_, _) => UpdateContinueEnabled();

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        if (_links.Count(l => l.HasUrl) > BatchThreshold)
        {
            BatchBar.Visibility = Visibility.Visible;
            IntroText.Text =
                Strings.ReadModPage_BatchNote;
        }

        UpdateContinueEnabled();
    }

    //
    // True when the user has turned the gate off on the Options page, in which case both entry
    // points below wave everything through.
    //
    // Checked here rather than at the half-dozen call sites: every path that installs anything goes
    // through Confirm or ConfirmAll, so this is the one place it cannot be forgotten - including by
    // whatever calls it next.
    //
    private static bool Skipped
    {
        get
        {
            if (!new SettingsService().Load().SkipModPageConfirmation) return false;

            AppLog.Info("ModPages", "gate skipped - turned off in Options");
            return true;
        }
    }

    //
    // Shows the gate for one mod and returns true only if Continue was clicked.
    //
    // <param name="allowSkip">False for a call that must ask even when the user has turned the gate
    // off in Options. Only this app's own update passes false: that page is where its release notes
    // are, so it is the one page where skipping costs the reader the thing they most need.</param>
    //
    public static bool Confirm(string modName, string? modPageUrl, bool allowSkip = true) =>
        (allowSkip && Skipped) || new ReadModPageConfirmationWindow(modName, modPageUrl).ShowDialog() == true;

    // Shows the gate for a batch of mods and returns true only if Continue was clicked.
    public static bool ConfirmAll(IReadOnlyList<ModPageLink> links) =>
        Skipped || new ReadModPageConfirmationWindow(links).ShowDialog() == true;

    private void UpdateContinueEnabled()
    {
        ContinueButton.IsEnabled = _openingSkipped || _links.All(l => l.IsOpened);

        if (BatchBar.Visibility != Visibility.Visible) return;

        var remaining = Unopened().Count;

        OpenBatchButton.IsEnabled = remaining > 0;
        SkipOpeningButton.IsEnabled = remaining > 0;

        if (remaining > 0)
        {
            OpenBatchButton.Content = remaining > BatchSize
                ? Text(Strings.ReadModPage_OpenNextFormat, BatchSize)
                : Strings.ReadModPage_OpenLast(remaining);
        }

        BatchProgress.Text = _openingSkipped
            ? Strings.ReadModPage_Skipping(remaining)
            : remaining == 0
                ? Text(Strings.ReadModPage_AllOpenedFormat, _links.Count)
                : Text(
                    Strings.ReadModPage_ProgressFormat,
                    _links.Count - remaining,
                    _links.Count,
                    remaining);
    }

    // Every link with a page that has not been opened yet, in list order.
    private List<ModPageLink> Unopened() => [.. _links.Where(l => l is { HasUrl: true, IsOpened: false })];

    private void OpenBatchButton_Click(object sender, RoutedEventArgs e) => Open(Unopened().Take(BatchSize).ToList());

    //
    // Waves the rest through. Deliberately not the same thing as the Options switch: it applies to
    // this batch only, and the pages already opened stay opened.
    //
    private void SkipOpeningButton_Click(object sender, RoutedEventArgs e)
    {
        _openingSkipped = true;
        AppLog.Info("ModPages", $"opening skipped for {Unopened().Count} of {_links.Count} page(s) in this batch");
        UpdateContinueEnabled();
    }

    //
    // The pages open inside the app (ModPageWindow), stepped through one at a time - the real
    // sp-mod.com page, so it still counts as a visit. Without a web view they open in the browser,
    // as they always did.
    //
    private void Open(IReadOnlyList<ModPageLink> links)
    {
        if (ModPageWindow.Show(this, links)) return;

        foreach (var link in links.Where(l => l.HasUrl))
        {
            Process.Start(new ProcessStartInfo(link.Url!) { UseShellExecute = true });
            link.IsOpened = true;
        }
    }

    private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModPageLink link } || !link.HasUrl) return;

        Open([link]);
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
