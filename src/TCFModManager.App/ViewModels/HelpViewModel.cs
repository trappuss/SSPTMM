using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Help;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.ViewModels;

//
// The Help page: every section of HelpCatalog, filtered by the search box.
//
// The search matches each topic's title and its hidden keywords, in the language on screen - so
// "uninstall" finds Remove. Steps are left out on purpose: nearly every step names a page, so
// searching them would turn "Installed" into half the page.
//
public partial class HelpViewModel : LocalizedViewModel
{
    public HelpViewModel()
    {
        Sections = new ObservableCollection<HelpSectionViewModel>(
            HelpCatalog.Sections.Select(s => new HelpSectionViewModel(s)));

        // Getting started opens expanded, so a first visit lands on steps rather than a list of titles.
        foreach (var topic in Sections[0].Topics) topic.IsExpanded = true;

        AppLanguage.Changed += (_, _) => ApplyFilter();

        // The install steps read differently in Monitor mode (R6), so a switch in Options rewords
        // the page the way a language switch does.
        AppServices.ModPageGate.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ModPageGateViewModel.IsDownloadOnly)) return;

            foreach (var section in Sections)
            {
                foreach (var topic in section.Topics) topic.Reword();
            }

            ApplyFilter();
        };
    }

    public ObservableCollection<HelpSectionViewModel> Sections { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasMatches = true;

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();

        // Clearing the search puts the page back as it opens, rather than leaving every match open.
        if (string.IsNullOrWhiteSpace(value)) CollapseAll();
    }

    //
    // Leaving the page: the search goes as well as every open how-to, so the next visit starts as the
    // page opens. Keeping the search would bring back its matches filtered in but closed.
    //
    public void Leave()
    {
        SearchText = string.Empty;
        CollapseAll();
    }

    // Leaving the page and clearing the search both close every how-to.
    public void CollapseAll()
    {
        foreach (var section in Sections)
        {
            foreach (var topic in section.Topics) topic.IsExpanded = false;
        }
    }

    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        var any = false;

        foreach (var section in Sections)
        {
            var sectionHasMatch = false;

            foreach (var topic in section.Topics)
            {
                topic.IsVisible = query.Length == 0
                    || compare.IndexOf(topic.Title, query, Loose) >= 0
                    || compare.IndexOf(topic.Keywords, query, Loose) >= 0;

                // A search opens what it found, so the answer is on screen without a second click.
                if (query.Length > 0) topic.IsExpanded = topic.IsVisible;

                sectionHasMatch |= topic.IsVisible;
            }

            section.IsVisible = sectionHasMatch;
            any |= sectionHasMatch;
        }

        HasMatches = any;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void OpenGuide() => OpenUrl(SelfMod.ModPageUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Help", $"couldn't open {url}: {ex.Message}");
        }
    }

    //
    // Arriving from the "?", F1 or the no-install banner: the search is cleared so the section is
    // actually there, and its how-tos open.
    //
    // Nothing else is closed. The page scrolls to the section straight after this, and a section
    // above it animating shut would move the target while the scroll is being measured.
    //
    public HelpSectionViewModel Arrive(string sectionId) => Arrive(sectionId, null).Section;

    // One topic rather than a whole section - a notice elsewhere pointing at the answer to it.
    public (HelpSectionViewModel Section, HelpTopicViewModel? Topic) Arrive(string sectionId, string? topicId)
    {
        SearchText = string.Empty;

        var target = Sections.FirstOrDefault(s => s.Id == sectionId) ?? Sections[0];
        var topic = topicId is null ? null : target.Topics.FirstOrDefault(t => t.Id == topicId);

        if (topic is not null) topic.IsExpanded = true;
        else foreach (var t in target.Topics) t.IsExpanded = true;

        return (target, topic);
    }

    // Before the page shows, so each Open button knows whether its page is in the sidebar right now.
    public void RefreshOpenButtons()
    {
        foreach (var section in Sections)
        {
            foreach (var topic in section.Topics) topic.RefreshOpen();
        }
    }

    [RelayCommand]
    private void ReportProblem() => OpenUrl(HelpCatalog.IssuesUrl);
}

public partial class HelpSectionViewModel : LocalizedViewModel
{
    private readonly HelpSection _section;

    internal HelpSectionViewModel(HelpSection section)
    {
        _section = section;
        Topics = section.Topics.Select(t => new HelpTopicViewModel(t, section.PageType)).ToList();
    }

    public string Id => _section.Id;

    public string Title => _section.Title();

    public SymbolRegular Icon => _section.Icon;

    public IReadOnlyList<HelpTopicViewModel> Topics { get; }

    [ObservableProperty]
    private bool _isVisible = true;
}

public partial class HelpTopicViewModel : LocalizedViewModel
{
    private readonly HelpTopic _topic;

    private readonly Type? _page;

    internal HelpTopicViewModel(HelpTopic topic, Type? sectionPage)
    {
        _topic = topic;
        _page = topic.PageSet ? topic.Page : sectionPage;
        Steps = topic.Steps.Select((s, i) => new HelpStepViewModel(s, i + 1)).ToList();
        Note = topic.Note is null ? null : new HelpStepViewModel(topic.Note, 0);
    }

    public string Id => _topic.Id;

    public string Title => _topic.Title();

    public string Keywords => _topic.Keywords?.Invoke() ?? string.Empty;

    // The Open button: the page the steps happen on, when there is one and it is in the sidebar.
    public bool CanOpenPage => _page is not null && HelpCatalog.CanOpen(_page);

    public string OpenPageLabel => _page is null
        ? string.Empty
        : LocalizationService.Text(Strings.Help_OpenPageFormat, HelpCatalog.PageName(_page));

    internal void RefreshOpen()
    {
        OnPropertyChanged(nameof(CanOpenPage));
        OnPropertyChanged(nameof(OpenPageLabel));
    }

    [RelayCommand]
    private void OpenPage()
    {
        if (_page is not null) AppNavigation.Navigate(_page);
    }

    public IReadOnlyList<HelpStepViewModel> Steps { get; }

    public HelpStepViewModel? Note { get; }

    // Re-reads the title and every step - HelpInlines rebuilds a step when it hears this.
    internal void Reword()
    {
        OnPropertyChanged(string.Empty);
        foreach (var step in Steps) step.Reword();
        Note?.Reword();
    }

    public bool HasNote => Note is not null;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isExpanded;
}

public class HelpStepViewModel : LocalizedViewModel
{
    private readonly HelpStep _step;

    internal HelpStepViewModel(HelpStep step, int number)
    {
        _step = step;
        Number = number;
    }

    public int Number { get; }

    public string NumberLabel => LocalizationService.Text(Strings.Help_StepNumberFormat, Number);

    // Read by the HelpInlines attached property, which turns them into Runs with the labels bold.
    internal IReadOnlyList<HelpRun> Runs => HelpText.Runs(_step);

    // Same text without the bold, for the screen reader and for anyone copying a step.
    public string PlainText => HelpText.Plain(_step);

    internal void Reword() => OnPropertyChanged(string.Empty);
}
