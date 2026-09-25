using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

//
// One row of Steam's tag filter: a [+] box, a [-] box and the tag's name. [+] shows only mods that
// have the tag, [-] hides every mod that has it.
//
// This app's filters each go one way - "Fika compatible only" narrows to a tag, "Hide mods with
// ads" hides one - so a row takes whichever direction the app has for that tag and leaves the
// other box disabled. The row keeps Steam's two-box shape either way, and the disabled box says
// plainly that the other direction is not a filter this app offers.
//
// Both boxes write straight through to the ModAttributeOption the rest of the page already filters
// on, so nothing about how filtering works has changed - only how it is asked for.
//
public sealed partial class WorkshopTagRow : LocalizedViewModel
{
    private readonly string _labelKey;

    public WorkshopTagRow(string labelKey, ModAttributeOption? include, ModAttributeOption? exclude)
    {
        _labelKey = labelKey;
        Include = include;
        Exclude = exclude;

        // The row repaints when either option changes from elsewhere - Clear filters, a saved default.
        if (include is not null) include.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IncludeOn));
        if (exclude is not null) exclude.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ExcludeOn));
    }

    public ModAttributeOption? Include { get; }

    public ModAttributeOption? Exclude { get; }

    public string Label => LocalizationService.Get(_labelKey);

    public bool CanInclude => Include is not null;

    public bool CanExclude => Exclude is not null;

    public string? IncludeToolTip => Include?.ToolTip ?? Include?.Label;

    public string? ExcludeToolTip => Exclude?.ToolTip ?? Exclude?.Label;

    public bool IncludeOn
    {
        get => Include?.IsSelected == true;
        set { if (Include is not null) Include.IsSelected = value; }
    }

    public bool ExcludeOn
    {
        get => Exclude?.IsSelected == true;
        set { if (Exclude is not null) Exclude.IsSelected = value; }
    }
}

//
// One entry in Steam's pager: a page number, or the "..." standing in for the ones skipped.
//
public sealed class PageLink
{
    public int? Number { get; init; }

    public bool IsCurrent { get; init; }

    public bool IsEllipsis => Number is null;

    // Numbers take the thousands separator, as Steam's "1,000" does.
    public string Text => Number is { } n ? n.ToString("N0") : "...";

    //
    // Steam's pattern: the first page, the last page, the current page and one either side of it,
    // with "..." wherever that leaves a gap. On page 1 of 1,000 that is "1 2 3 ... 1,000".
    //
    public static List<PageLink> For(int current, int total)
    {
        var numbers = new SortedSet<int> { 1, total };

        for (var n = current - 1; n <= current + 1; n++)
        {
            if (n >= 1 && n <= total) numbers.Add(n);
        }

        // Steam shows three leading pages while you are at the very start.
        if (current == 1 && total >= 3) numbers.Add(3);

        var links = new List<PageLink>();
        var previous = 0;

        foreach (var n in numbers)
        {
            if (previous != 0 && n - previous > 1) links.Add(new PageLink());
            links.Add(new PageLink { Number = n, IsCurrent = n == current });
            previous = n;
        }

        return links;
    }
}
