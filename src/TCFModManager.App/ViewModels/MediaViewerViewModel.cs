using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Markup;

namespace TCFModManager.App.ViewModels;

//
// The full-size viewer over the whole window: Steam's screenshot and video viewer on an item page.
// Opened from the screenshot strip or from a picture or video in a description, it steps through
// every picture and video of that description.
//
public sealed partial class MediaViewerViewModel : ObservableObject
{
    public ObservableCollection<MarkupMedia> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current))]
    [NotifyPropertyChangedFor(nameof(Counter))]
    [NotifyPropertyChangedFor(nameof(IsVideo))]
    [NotifyPropertyChangedFor(nameof(IsPicture))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private int _index = -1;

    [ObservableProperty]
    private bool _isOpen;

    public MarkupMedia? Current => Index >= 0 && Index < Items.Count ? Items[Index] : null;

    public bool IsVideo => Current?.Kind == MarkupMediaKind.Video;

    public bool IsPicture => Current?.Kind == MarkupMediaKind.Image;

    public bool HasMany => Items.Count > 1;

    // "3 / 12", as Steam numbers its viewer.
    public string Counter => Items.Count == 0 ? string.Empty : LocalizationService.Text(Strings.Viewer_CounterFormat, Index + 1, Items.Count);

    public void Show(IReadOnlyList<MarkupMedia> gallery, string url)
    {
        Items.Clear();
        foreach (var item in gallery) Items.Add(item);

        var at = Items.ToList().FindIndex(m => string.Equals(m.Url, url, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
        {
            // A picture that is not in the gallery (it failed to parse as one) still opens, alone.
            Items.Clear();
            Items.Add(new MarkupMedia(MarkupMediaKind.Image, url, url, null, null));
            at = 0;
        }

        OnPropertyChanged(nameof(HasMany));
        Index = -1;
        Index = at;
        IsOpen = true;
    }

    private bool CanGoPrevious() => Index > 0;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Index--;

    private bool CanGoNext() => Index < Items.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Index++;

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Index = -1;
    }

    // The picture's own address, or the video on YouTube.
    [RelayCommand]
    private void OpenInBrowser()
    {
        if (Current is { } media) MarkupActions.OpenInBrowser(media.Url);
    }
}
