using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public sealed class FikaModeItem(string key, bool? value) : LocalizedViewModel
{
    // Null for Automatic.
    public bool? Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

//
// Fork (1.4.3, experimental): Options' "Play with Fika" row - what the Play card offers for other
// PCs' servers and a Fika host (PlayViewModel.Fika). Automatic follows whether Fika is installed.
//
public partial class OptionsViewModel
{
    public IReadOnlyList<FikaModeItem> FikaModeOptions { get; } =
    [
        new(nameof(Strings.Options_FikaAutomatic), null),
        new(nameof(Strings.Options_FikaOn), true),
        new(nameof(Strings.Options_FikaOff), false),
    ];

    [ObservableProperty]
    private FikaModeItem _selectedFikaMode = null!;

    partial void OnSelectedFikaModeChanged(FikaModeItem value)
    {
        if (!_loaded || value is null) return;

        var settings = _settings.Load();
        settings.FikaOptions = value.Value;
        _settings.Save(settings);

        AppLog.Info("Fika", $"Play with Fika: {value.Value switch { null => "automatic", true => "on", false => "off" }}");
    }

    // Fork (1.4.8): Copy invite may ask an outside service for this PC's public address.
    [ObservableProperty]
    private bool _fikaInvitePublicAddress;

    partial void OnFikaInvitePublicAddressChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.FikaInvitePublicAddress = value;
        _settings.Save(settings);

        AppLog.Info("Fika", $"public address in the invite: {(value ? "on" : "off")}");
    }

    // Null until looked for.
    private FikaInstall.Found? _fikaInInstall;

    public string FikaFoundText => string.IsNullOrWhiteSpace(SptEnvironment.InstallPath)
        ? Strings.Options_FikaNoInstall
        : _fikaInInstall switch
        {
            null => string.Empty,
            { Client: true, Server: true } => Strings.Options_FikaFoundBoth,
            { Client: true } => Strings.Options_FikaFoundClient,
            { Server: true } => Strings.Options_FikaFoundServer,
            _ => Strings.Options_FikaNotFound,
        };

    // Before _loaded: setting it here saves nothing.
    private void LoadFika(AppSettings settings)
    {
        SelectedFikaMode = FikaModeOptions.FirstOrDefault(o => o.Value == settings.FikaOptions) ?? FikaModeOptions[0];
        FikaInvitePublicAddress = settings.FikaInvitePublicAddress;

        SptEnvironment.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SptEnvironmentViewModel.InstallPath)) _ = LookForFikaAsync();
        };
        _ = LookForFikaAsync();
    }

    private async Task LookForFikaAsync()
    {
        var path = SptEnvironment.InstallPath;
        _fikaInInstall = string.IsNullOrWhiteSpace(path) ? null : await FikaInstall.ReadAsync(path, fresh: true);
        OnPropertyChanged(nameof(FikaFoundText));
    }
}
