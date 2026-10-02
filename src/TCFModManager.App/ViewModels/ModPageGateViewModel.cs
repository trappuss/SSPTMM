using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.ViewModels;

//
// Whether the "read the mod's page first" gate is currently switched off, and the wording every
// install button uses to say so.
//
// Also carries Monitor mode's install mode, for the same reason: it decides what every install
// button does and says. The main button follows the mode, and the small button beside it does the
// other thing for that one mod (R4).
//
// A shared singleton rather than a property on each mod's card view model: the setting is one global
// choice, so a hundred Browse cards shouldn't each be reading settings.json to answer the same
// question - and when it changes, every button needs to re-read it at once.
//
public sealed partial class ModPageGateViewModel : LocalizedViewModel
{
    // Read on every get, so a language change relabels the tooltips with the rest of the app.
    private static string SkipNotice => $"\n\n{Strings.ModPageGate_SkipNotice}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallToolTip))]
    [NotifyPropertyChangedFor(nameof(RedownloadToolTip))]
    [NotifyPropertyChangedFor(nameof(UpdateToolTip))]
    [NotifyPropertyChangedFor(nameof(SettingToolTip))]
    [NotifyPropertyChangedFor(nameof(AlternateToolTip))]
    private bool _isSkipping;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallToolTip))]
    [NotifyPropertyChangedFor(nameof(RedownloadToolTip))]
    [NotifyPropertyChangedFor(nameof(UpdateToolTip))]
    [NotifyPropertyChangedFor(nameof(AlternateToolTip))]
    [NotifyPropertyChangedFor(nameof(MainSymbol))]
    [NotifyPropertyChangedFor(nameof(AlternateSymbol))]
    private bool _isDownloadOnly;

    private string Notice => IsSkipping ? SkipNotice : string.Empty;

    public string InstallToolTip =>
        (IsDownloadOnly ? Strings.ModPageGate_InstallDownloadOnly : Strings.ModPageGate_Install) + Notice;

    public string RedownloadToolTip =>
        (IsDownloadOnly ? Strings.ModPageGate_RedownloadDownloadOnly : Strings.ModPageGate_Redownload) + Notice;

    public string UpdateToolTip =>
        (IsDownloadOnly ? Strings.ModPageGate_UpdateDownloadOnly : Strings.ModPageGate_Update) + Notice;

    // The small button beside each install button, which does the opposite of the mode.
    public string AlternateToolTip =>
        (IsDownloadOnly ? Strings.ModPageGate_AlternateInstall : Strings.ModPageGate_AlternateDownload) + Notice;

    // Install keeps the icon it has always had; a download-only action is a file being saved.
    //
    // Only a symbol whose code point fits in 16 bits works: WPF-UI 4.3.0 truncates the rest, so
    // DocumentArrowDown24 (0xF0527) drew as the Cyrillic letter at 0x0527.
    public SymbolRegular MainSymbol => IsDownloadOnly ? SymbolRegular.DocumentSave24 : SymbolRegular.ArrowDownload24;

    public SymbolRegular AlternateSymbol => IsDownloadOnly ? SymbolRegular.ArrowDownload24 : SymbolRegular.DocumentSave24;

    // What a click on the main button does; the alternate button passes the opposite.
    public bool DownloadOnlyFor(bool alternate) => IsDownloadOnly != alternate;

    // The Options page's own switch, kept here so there is one description of what the setting
    // currently means rather than two that can drift apart.
    public string SettingToolTip => IsSkipping
        ? Strings.Options_ModPagesToolTipSkipping
        : Strings.Options_ModPagesToolTipAsking;

    // Re-reads both settings. Called at startup and whenever the Options page changes either.
    public void Refresh()
    {
        var settings = new SettingsService().Load();
        IsSkipping = settings.SkipModPageConfirmation;
        IsDownloadOnly = settings.Monitor.InstallMode == InstallMode.DownloadOnly;
    }
}
