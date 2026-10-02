using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One installed mod on a machine's card, as it reported it.
public sealed record MapModLine(string Name, string Detail);

//
// One machine on the Server map page.
//
// Everything shown is computed from what the server sent and the list this install holds, so a
// language change re-reads it rather than leaving a card half in the old language. The card is
// rebuilt on every refresh; nothing on it is edited.
//
public sealed class MapMachineViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly MapMachine _machine;
    private readonly int _intervalSeconds;
    private readonly ModList? _list;
    private readonly MachineStanding? _standing;
    private readonly IReadOnlyList<ReportedMod> _mods;

    public MapMachineViewModel(MapMachine machine, int intervalSeconds, ModList? list)
    {
        _machine = machine;
        _intervalSeconds = intervalSeconds;
        _list = list;
        _standing = list is null ? null : ServerMapMachines.StandingOf(list, machine);

        ReviewCommand = new RelayCommand(() => { if (_list is not null) AppNavigation.ReviewList(_list.Id); });

        _mods = (machine.Mods ?? [])
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string DisplayName => _machine.DisplayName;

    public bool IsYou => _machine.IsYou;

    // The machine's own icon says what it mostly is: the host first, then a player, then a headless.
    public string RoleGlyph => _machine.Hosts ? "Server24" : _machine.Plays || !_machine.Headless ? "Desktop24" : "Bot24";

    public string RolesText
    {
        get
        {
            var roles = new List<string>();
            if (_machine.Hosts) roles.Add(Strings.ServerMap_RoleServer);
            if (_machine.Plays) roles.Add(Strings.ServerMap_RolePlayer);
            if (_machine.Headless) roles.Add(Strings.ServerMap_RoleHeadless);
            if (roles.Count == 0) roles.Add(Strings.ServerMap_RolePlayer);

            var parts = new List<string> { string.Join(Strings.Common_ListSeparator, roles) };

            if (!string.IsNullOrWhiteSpace(_machine.SptVersion))
                parts.Add(Text(Strings.ServerMap_DetailSptFormat, _machine.SptVersion));

            if (!string.IsNullOrWhiteSpace(_machine.AppVersion))
                parts.Add(Text(Strings.ServerMap_AppVersionFormat, _machine.AppVersion));

            return string.Join(Strings.ServerMap_DetailSeparator, parts);
        }
    }

    public MapPresence Presence => ServerMapMachines.PresenceOf(_machine, _intervalSeconds);

    public string PresenceGlyph => Presence switch
    {
        MapPresence.InGame => "Games24",
        MapPresence.AppOpen => "PlugConnected24",
        _ => "Clock24",
    };

    public string PresenceText => Presence switch
    {
        MapPresence.InGame => Strings.ServerMap_PresenceInGame,
        MapPresence.AppOpen => Strings.ServerMap_PresenceAppOpen,
        _ => LastSeen(_machine.SecondsSinceSeen),
    };

    //
    // The shared status vocabulary, so a machine reads the way a mod does everywhere else: green when
    // it has everything, amber when something needs updating or enabling, red when something is
    // missing, grey when it is not known.
    //
    public ModStatus Status
    {
        get
        {
            if (_standing is null) return ModStatus.Unknown;
            if (_standing.Behind.Any(a => a.Kind == ModListActionKind.Install)) return ModStatus.NotInstalled;
            if (_standing.Behind.Count > 0) return ModStatus.UpdateAvailable;
            if (_standing.Manual.Count > 0) return ModStatus.NoCompatibleVersion;
            return ModStatus.Installed;
        }
    }

    public string StatusGlyph => ModStatusDisplay.Glyph(Status);

    public string StandingText
    {
        get
        {
            if (_list is null) return Strings.ServerMap_StandingNoList;
            if (_machine.InventoryUnreadable) return Strings.ServerMap_StandingUnreadable;
            if (_standing is null) return Strings.ServerMap_StandingNotReported;

            if (_standing.UpToDate) return Strings.ServerMap_StandingUpToDate(_standing.Expected, _standing.Expected, _list.Name);

            var parts = new List<string>();

            if (_standing.Behind.Count > 0)
                parts.Add(Strings.ServerMap_StandingBehind(_standing.Behind.Count, _standing.Behind.Count, Names(_standing.Behind)));

            if (_standing.Manual.Count > 0)
                parts.Add(Strings.ServerMap_StandingManual(_standing.Manual.Count, _standing.Manual.Count, Names(_standing.Manual)));

            return string.Join(Environment.NewLine, parts);
        }
    }

    //
    // On this machine's own card only, when it is behind a list a server served. Never on the host's
    // card: the host compares against the operator's own Local copy, and previewing that as their
    // own list would sweep every server mod off the box.
    //
    public bool CanReview =>
        IsYou && _list is { Origin: ModListOrigin.Server } && _standing is { UpToDate: false };

    public IRelayCommand ReviewCommand { get; }

    public bool HasExtras => _standing is { Extras.Count: > 0 };

    public string ExtrasText => _standing is { Extras.Count: > 0 } s
        ? Strings.ServerMap_Extras(s.Extras.Count, s.Extras.Count,
            string.Join(Strings.Common_ListSeparator, s.Extras.Select(m => m.Name).Order(StringComparer.OrdinalIgnoreCase)))
        : "";

    // Composed on every read, so "disabled" and "unknown" follow a language change like the rest of
    // the card.
    public IReadOnlyList<MapModLine> Mods => _mods.Select(m => new MapModLine(m.Name, ModDetail(m))).ToList();

    public bool HasMods => _mods.Count > 0;

    public string ModsHeader => Strings.ServerMap_InstalledCount(_mods.Count, _mods.Count);

    private static string Names(IEnumerable<ModListAction> actions) =>
        string.Join(Strings.Common_ListSeparator, actions.Select(ModListActionWording.Named).Order(StringComparer.OrdinalIgnoreCase));

    private static string ModDetail(ReportedMod mod)
    {
        var version = string.IsNullOrWhiteSpace(mod.Version) ? Strings.Common_Unknown : mod.Version!;
        return mod.Disabled ? Text(Strings.ServerMap_ModDisabledFormat, version) : version;
    }

    private static string LastSeen(long seconds)
    {
        var minutes = (int)Math.Min(int.MaxValue, seconds / 60);

        if (minutes < 60) return Strings.ServerMap_LastSeenMinutes(Math.Max(1, minutes));

        var hours = minutes / 60;
        if (hours < 48) return Strings.ServerMap_LastSeenHours(hours);

        return Strings.ServerMap_LastSeenDays(hours / 24);
    }
}
