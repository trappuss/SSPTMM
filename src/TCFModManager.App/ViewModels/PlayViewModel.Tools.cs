using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>One row of the Play page's Mod tools: a mod's .exe, with what it needs from the server.</summary>
public sealed partial class ModToolRow : ObservableObject
{
    public ModToolRow(ModTool tool, ImageSource? icon)
    {
        Tool = tool;
        Icon = icon;
        _isHidden = tool.IsHidden;
    }

    public ModTool Tool { get; }

    public ImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    public string Name => Tool.Name;

    public string FromText => LocalizationService.Text(Strings.Play_ToolFromFormat, Tool.ModName);

    public bool CanOpen => !Tool.IsModDisabled;

    public string? DisabledText => Tool.IsModDisabled ? LocalizationService.Text(Strings.Play_ToolModDisabledFormat, Tool.ModName) : null;

    [ObservableProperty]
    private bool _isHidden;

    // What it needs from the server, said against whether the server is up right now.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string? _note;

    [ObservableProperty]
    private bool _noteIsCaution;

    // A tool that needs the server, while it is down: Start server beside it.
    [ObservableProperty]
    private bool _showStartServer;

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public void UpdateServer(bool serverUp)
    {
        switch (Tool.Need)
        {
            case ModToolNeed.ServerRunning:
                Note = serverUp ? Strings.Play_ToolNeedsServerRunning : Strings.Play_ToolNeedsServerRunningDown;
                NoteIsCaution = !serverUp;
                ShowStartServer = !serverUp && CanOpen;
                break;

            case ModToolNeed.ServerStopped:
                Note = serverUp ? Strings.Play_ToolNeedsServerStoppedUp : Strings.Play_ToolNeedsServerStopped;
                NoteIsCaution = serverUp;
                ShowStartServer = false;
                break;

            default:
                Note = null;
                NoteIsCaution = false;
                ShowStartServer = false;
                break;
        }
    }
}

//
// Fork (SSPTMM): the Play page's Mod tools card - every .exe an installed mod put in the SPT folder
// (ModTools), filled by itself, each with Open, Open folder and Hide. Found when the page is shown and
// again whenever the installed mods change, off the UI thread: the scan reads the mods' folders.
//
public partial class PlayViewModel
{
    public ObservableCollection<ModToolRow> Tools { get; } = [];

    private List<ModToolRow> _allTools = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHiddenToolsText))]
    private bool _showingHiddenTools;

    // The card shows while there is anything to list, hidden tools included - so they can come back.
    public bool HasTools => _allTools.Count > 0;

    public bool HasHiddenTools => _allTools.Any(t => t.IsHidden);

    public string ShowHiddenToolsText
    {
        get
        {
            var hidden = _allTools.Count(t => t.IsHidden);
            return ShowingHiddenTools ? Strings.Play_ToolsHideHidden : Strings.Play_ToolsShowHidden(hidden, hidden);
        }
    }

    private bool _toolsHooked;
    private int _toolsGeneration;

    public async Task RefreshToolsAsync()
    {
        if (!_toolsHooked)
        {
            _toolsHooked = true;
            AppServices.Browse.InstalledIndexChanged += (_, _) => _ = RefreshToolsAsync();
        }

        var generation = ++_toolsGeneration;
        var installPath = AppServices.SptEnvironment.InstallPath;

        List<ModTool> found;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            found = [];
        }
        else
        {
            var hidden = new SettingsService().Load().HiddenModTools;
            try
            {
                found = await Task.Run(() => ModTools.Find(
                    installPath, AppServices.InstallManifest.Load().Mods, InstalledModScanner.Scan(installPath), hidden));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Tools", $"couldn't look for mod tools: {ex.Message}");
                found = [];
            }
        }

        // A later refresh started while this one ran: its answer is the one to show.
        if (generation != _toolsGeneration) return;

        _allTools = [.. found.Select(t => new ModToolRow(t, ExeIcons.For(t.FullPath)))];
        foreach (var row in _allTools) row.UpdateServer(IsServerRunning);
        ShowTools();
    }

    private void ShowTools()
    {
        Tools.Clear();
        foreach (var row in _allTools.Where(r => !r.IsHidden || ShowingHiddenTools)) Tools.Add(row);

        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(HasHiddenTools));
        OnPropertyChanged(nameof(ShowHiddenToolsText));
    }

    // From Refresh, every poll: the notes follow the server going up and down.
    private void UpdateToolStates()
    {
        foreach (var row in _allTools) row.UpdateServer(IsServerRunning);
    }

    [RelayCommand]
    private void OpenTool(ModToolRow? row)
    {
        if (row is not { CanOpen: true }) return;

        try
        {
            // From its own folder: tools like SVM's Greed look for their files beside them. Through the
            // shell, so Windows handles anything the tool asks for - its own elevation, a missing runtime.
            Process.Start(new ProcessStartInfo(row.Tool.FullPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(row.Tool.FullPath),
            })?.Dispose();

            AppLog.Info("Tools", $"opened {row.Tool.FullPath}");
            HasError = false;
            Message = LocalizationService.Text(Strings.Play_ToolOpenedFormat, row.Name);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            AppLog.Warn("Tools", $"couldn't open {row.Tool.FullPath}: {ex.Message}");
            HasError = true;
            Message = LocalizationService.Text(Strings.Play_ToolOpenFailedFormat, row.Name, ex.Message);
        }
    }

    [RelayCommand]
    private void OpenToolFolder(ModToolRow? row)
    {
        if (row is null) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{row.Tool.ModFolder}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            HasError = true;
            Message = LocalizationService.Text(Strings.Play_ToolOpenFailedFormat, row.Tool.ModFolder, ex.Message);
        }
    }

    [RelayCommand]
    private void HideTool(ModToolRow? row) => SetHidden(row, hidden: true);

    [RelayCommand]
    private void UnhideTool(ModToolRow? row) => SetHidden(row, hidden: false);

    [RelayCommand]
    private void ToggleHiddenTools()
    {
        ShowingHiddenTools = !ShowingHiddenTools;
        ShowTools();
    }

    private void SetHidden(ModToolRow? row, bool hidden)
    {
        if (row is null) return;

        var service = new SettingsService();
        var settings = service.Load();
        settings.HiddenModTools.RemoveAll(k => string.Equals(k, row.Tool.Key, StringComparison.OrdinalIgnoreCase));
        if (hidden) settings.HiddenModTools.Add(row.Tool.Key);
        service.Save(settings);

        row.IsHidden = hidden;
        if (!_allTools.Any(t => t.IsHidden)) ShowingHiddenTools = false;
        ShowTools();
    }
}
