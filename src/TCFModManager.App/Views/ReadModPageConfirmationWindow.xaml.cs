using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Views;

// One row in ReadModPageConfirmationWindow - a mod name, the button that opens its sp-mod.com page,
// and, for a mod or addon the app can look up, its page to read right there (Read here).
public sealed class ModPageLink(string name, string? url) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public string? Url { get; } = url;
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    /// <summary>The sp-mod.com id of the mod (or addon) whose page Read here shows.</summary>
    public int? ModId { get; init; }

    public bool IsAddon { get; init; }

    /// <summary>For an update: the version whose change notes Read here shows, rather than the
    /// whole page - what changed is what an update asks the reader to know.</summary>
    public string? ChangeNotesVersion { get; init; }

    public bool CanRead => ModId is > 0;

    /// <summary>Already seen in the app - the item page Subscribe was clicked on - so the dialog
    /// leaves it out.</summary>
    public bool Seen { get; init; }

    private bool _isOpened = string.IsNullOrWhiteSpace(url);
    public bool IsOpened
    {
        get => _isOpened;
        set
        {
            if (_isOpened == value) return;
            _isOpened = value;
            Changed(nameof(IsOpened));
            Changed(nameof(ButtonLabel));
        }
    }

    public string ButtonLabel => !HasUrl
        ? Strings.ReadModPage_ButtonNoPage
        : IsOpened ? Strings.ReadModPage_ButtonOpened : Strings.ReadModPage_ButtonOpen;

    // ------------------------------------------------------------------ Read here

    private bool _isReading;
    public bool IsReading
    {
        get => _isReading;
        set
        {
            if (_isReading == value) return;
            _isReading = value;
            Changed(nameof(IsReading));
            Changed(nameof(ReadLabel));
        }
    }

    public string ReadLabel => IsReading ? Strings.ReadModPage_Hide : Strings.ReadModPage_Read;

    /// <summary>"Change notes for 1.5.0" over an update's notes; nothing over a whole page.</summary>
    public string? ReadHeading => ChangeNotesVersion is { } version
        ? LocalizationService.Text(Strings.ReadModPage_ChangeNotesFormat, version)
        : null;

    private string? _html;
    public string? Html
    {
        get => _html;
        set { _html = value; Changed(nameof(Html)); }
    }

    // Loading, or why there is nothing to show; null once the page is there.
    private string? _readNote;
    public string? ReadNote
    {
        get => _readNote;
        set { _readNote = value; Changed(nameof(ReadNote)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>A row for a mod: its name, page, and page to read here.</summary>
    public static ModPageLink For(Core.Models.Mod mod, string? name = null) =>
        new(name ?? mod.Name ?? string.Empty, mod.DetailUrl) { ModId = mod.Id };
}

//
// Shown before mods are queued for install or update: each one's page - where its author puts
// install steps, requirements, known conflicts and warnings - to read right here (Read here, the
// first one already open), or to open on sp-mod.com. Continue is there from the start: the page is
// in front of the reader, which is what opening it in a browser was for, without the trip.
//
// The app's own update is the exception (RequireOpening): its page carries the release notes, so
// that one keeps asking for it to be opened before Continue unlocks, as every page used to.
//
// A plain Window drawn as Steam's modal dialog, so it can be shown via ShowDialog() from any call
// site - see Ask.
//
public partial class ReadModPageConfirmationWindow : Window
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

    // Continue waits for every page to be opened - the app's own update only.
    private readonly bool _requireOpening;

    public ReadModPageConfirmationWindow(IReadOnlyList<ModPageLink> links, bool requireOpening = false)
    {
        _links = links.ToList();
        _requireOpening = requireOpening;
        InitializeComponent();

        TitleText.Text = Title = _links.Count == 1
            ? Text(Strings.ReadModPage_TitleNamedFormat, _links[0].Name)
            : Text(Strings.ReadModPage_TitleCountFormat, _links.Count);

        LinksList.ItemsSource = _links;
        foreach (var link in _links) link.PropertyChanged += (_, _) => UpdateContinueEnabled();

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        if (!_requireOpening)
        {
            // Wider, and taller in its list, for pages read here - as tall as fits inside the
            // window it opens over, leaving room for the title, the text and the buttons.
            Width = 760;
            var room = (Owner is { ActualHeight: > 0 } owner ? owner.ActualHeight : SystemParameters.WorkArea.Height) - 360;
            LinksScroller.MaxHeight = Math.Clamp(room, 320, 640);
            IntroText.Text = Strings.ReadModPage_ReaderIntro;

            // The first page open already: what the dialog is for is on screen when it appears.
            Loaded += (_, _) =>
            {
                if (_links.FirstOrDefault(l => l.CanRead) is { } first) _ = ReadAsync(first);
            };

            // A page opening makes the dialog taller after it was centred on its first, shorter
            // height; centred again each time, it stays over the window and its buttons on screen.
            SizeChanged += (_, e) =>
            {
                if (!e.HeightChanged || Owner is not { } over) return;

                var top = over.WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Top : over.Top;
                var height = over.WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Height : over.ActualHeight;
                Top = Math.Max(SystemParameters.VirtualScreenTop, top + (height - ActualHeight) / 2);
            };
        }
        else if (_links.Count(l => l.HasUrl) > BatchThreshold)
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
        (allowSkip && Skipped) || Ask(new ReadModPageConfirmationWindow([new ModPageLink(modName, modPageUrl)], requireOpening: !allowSkip));

    // One mod, with its page to read here.
    public static bool Confirm(ModPageLink link) => ConfirmAll([link]);

    //
    // Shows the gate for a batch of mods and returns true only if Continue was clicked. Pages
    // already seen in the app (Seen - the item page the Subscribe was clicked on) are left out; with
    // none left, nothing is asked.
    //
    public static bool ConfirmAll(IReadOnlyList<ModPageLink> links)
    {
        var unseen = links.Where(l => !l.Seen).ToList();
        if (unseen.Count == 0) return true;

        return Skipped || Ask(new ReadModPageConfirmationWindow(unseen));
    }

    // Over the main window, dimmed behind it as Steam dims the page behind its dialogs.
    private static bool Ask(ReadModPageConfirmationWindow gate)
    {
        var dimmed = gate.Owner as MainWindow;
        dimmed?.SetModalDim(true);
        try
        {
            return gate.ShowDialog() == true;
        }
        finally
        {
            dimmed?.SetModalDim(false);
        }
    }

    private void UpdateContinueEnabled()
    {
        ContinueButton.IsEnabled = !_requireOpening || _openingSkipped || _links.All(l => l.IsOpened);

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

    // ------------------------------------------------------------------ Read here

    // Each page read this session, so a second dialog for the same mod shows it at once. Keyed by
    // mod or addon, id and - for change notes - version.
    private static readonly Dictionary<string, string> Pages = [];

    private void ReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModPageLink link }) return;

        if (link.IsReading) link.IsReading = false;
        else _ = ReadAsync(link);
    }

    // Shows the link's page (or its change notes) in its row, fetching it the first time.
    private static async Task ReadAsync(ModPageLink link)
    {
        link.IsReading = true;
        if (link.Html is not null || link.ModId is not { } id) return;

        var key = string.Join('|', link.IsAddon, id, link.ChangeNotesVersion);
        if (Pages.TryGetValue(key, out var known))
        {
            Show(link, known);
            return;
        }

        link.ReadNote = Strings.ReadModPage_Loading;
        try
        {
            var html = await FetchAsync(link, id);
            Pages[key] = html;
            Show(link, html);
        }
        catch (Exception ex)
        {
            // Offline, rate limited, gone: the page can still be opened on sp-mod.com.
            AppLog.Warn("ModPages", $"couldn't load the page of {link.Name} to read here: {ex.Message}");
            link.ReadNote = Strings.ReadModPage_LoadFailed;
        }
    }

    private static void Show(ModPageLink link, string html)
    {
        link.ReadNote = string.IsNullOrWhiteSpace(html) ? Strings.ReadModPage_Empty : null;
        link.Html = html;
    }

    // The description exactly as sp-mod.com has it - what its page shows - or one version's
    // change notes. A mod with no description of its own shows its teaser, as the item page does.
    private static async Task<string> FetchAsync(ModPageLink link, int id)
    {
        var api = AppServices.SpModApi;

        if (link.ChangeNotesVersion is { } version)
        {
            string? notes;
            if (link.IsAddon)
            {
                var found = await api.GetAddonVersionsAsync(id.ToString(), new Core.SpModApi.AddonVersionsQuery { FilterVersion = version, PerPage = 5 });
                notes = found.Data.FirstOrDefault(v => v.Version == version)?.Description;
            }
            else
            {
                var found = await api.GetModVersionsAsync(id.ToString(), new Core.SpModApi.ModVersionsQuery { FilterVersion = version, PerPage = 5 });
                notes = found.Data.FirstOrDefault(v => v.Version == version)?.Description;
            }

            return notes ?? string.Empty;
        }

        if (link.IsAddon)
        {
            var addon = await api.GetAddonAsync(id.ToString());
            return !string.IsNullOrWhiteSpace(addon.Description)
                ? addon.Description
                : System.Net.WebUtility.HtmlEncode(addon.Teaser ?? string.Empty);
        }

        var mod = await api.GetModAsync(id.ToString());
        return !string.IsNullOrWhiteSpace(mod.Description)
            ? mod.Description
            : System.Net.WebUtility.HtmlEncode(mod.Teaser ?? string.Empty);
    }

    private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModPageLink link } || !link.HasUrl) return;

        Open([link]);
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;

        e.Handled = true;
        DialogResult = false;
    }
}
