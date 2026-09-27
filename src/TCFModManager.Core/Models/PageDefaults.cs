namespace TCFModManager.Core.Models;

//
// The filter/sort/layout state a page opens with, captured from the page itself by its "Save as
// default" button rather than restated as a second set of dropdowns in Options.
//
// Every value is a string of the App project's own enum name, not the enum. Those enums live in
// TCFModManager.App (they are dropdown entries, and several of them - ModAttributeFilter, the view
// modes - describe UI rather than data), and Core cannot reference App. Strings also keep
// settings.json readable, which is the same reason ThemePreference is written as a name: this file
// is offered for hand-editing on the Options page.
//
// A name that no longer parses - a filter renamed or removed between releases - is ignored and the
// app's own default is used, so an old settings.json never stops a page from opening.
//
// Null throughout means "not saved, use the app's default". Search text is deliberately not here:
// a page that opens already filtered to a phrase you typed weeks ago looks broken.
//
public sealed class InstalledPageDefaults
{
    // InstalledViewMode: Cards, Groups or List.
    public string? ViewMode { get; set; }

    // UpdateFilter: All, NeedsUpdate, UpToDate, NotFound.
    public string? UpdateStatus { get; set; }

    // EnabledFilter: All, EnabledOnly, DisabledOnly.
    public string? Enabled { get; set; }

    // The category's own title as sp-mod.com writes it, not an enum. Null means all categories.
    public string? Category { get; set; }

    //
    // "all", "ungrouped", or a group's Guid. A saved group that has since been deleted falls back
    // to all groups - the same thing the dropdown itself does with a stale assignment.
    //
    public string? Group { get; set; }

    // ModSortOption: NameAscending, NameDescending, AuthorAscending, AuthorDescending,
    // GroupAscending, GroupDescending, RecentlyInstalled.
    public string? Sort { get; set; }

    // GroupSortOption: Manual, NameAscending, NameDescending, Category.
    public string? GroupSort { get; set; }

    // InstalledGrouping, for the Cards and List views: None, Groups, Category.
    public string? Grouping { get; set; }

    // One of the page's own PageSizeOptions. A value that isn't in that list is ignored.
    public int? PageSize { get; set; }

    // ModAttributeFilter names for the ticked boxes only. An empty list is a real answer: it means
    // the default is no attribute filtering, which is not the same as never having saved one.
    public List<string> Attributes { get; set; } = [];
}

//
// The same idea for Browse. A separate type rather than a shared one because the two pages filter
// on genuinely different things - Browse has SPT versions and Featured, Installed has groups and
// update status - and a merged type would be half-empty from either side.
//
public sealed class BrowsePageDefaults
{
    // ModSortOrder: Newest, LastUpdated, MostDownloaded, MostFavourited, MostEndorsed.
    public string? Sort { get; set; }

    // FeaturedFilter: Include, Exclude, Only.
    public string? Featured { get; set; }

    public string? Category { get; set; }

    public int? PageSize { get; set; }

    public List<string> Attributes { get; set; } = [];

    //
    // The ticked SPT release lines as major.minor ("3.11", "4.0"), or null for the app's own
    // default of pre-ticking whichever line the detected install is on.
    //
    // Null and empty mean different things here, which is why this one is nullable and Attributes
    // is not: an empty list is "show every SPT version", a deliberate and useful default, while
    // null is "follow the install" and is what a fresh install does.
    //
    public List<string>? SptVersions { get; set; }
}
