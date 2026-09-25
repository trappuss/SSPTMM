using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;

namespace TCFModManager.App.Behaviors;

//
// Steam's hover popup on a Workshop item card - Browse's grid and the front page's carousel, lists
// and "From Followed Authors" all have it on Steam, so they all have it here:
// behaviors:CardHover.Enabled="True" on the card, whose DataContext is a ModCardViewModel.
//
// The popup's content (CardHoverPopup) is built the first time it opens rather than with every
// card, so a page of cards costs nothing for it until one is pointed at.
//
// While it is open it runs the slideshow: the mod's own picture at once, then - if the pointer
// stays a moment, so a pass across the page does not ask sp-mod.com about every card it crosses -
// the pictures in its description, one after another every two seconds. Only pictures that loaded
// and decoded take part, so the slideshow never stops on a blank; with only one there is none.
// One popup is open at a time, so one slideshow runs, whichever page it is on.
//
public static class CardHover
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(CardHover), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement card || e.NewValue is not true || card.ToolTip is ToolTip) return;

        // Steam's popup: #344352, a 4px 4px 10px shadow, 5 8 inside, to the right of the card.
        // No MaxWidth: WPF UI's tooltip style caps it narrower than Steam's 270px, which cut the
        // popup's right edge (its text and pictures) off.
        var popup = new ToolTip
        {
            Padding = new Thickness(8, 5, 8, 5),
            HasDropShadow = true,
            MaxWidth = double.PositiveInfinity,
        };
        popup.SetResourceReference(Control.BackgroundProperty, "SteamPopupBackground");
        popup.Opened += Popup_Opened;
        popup.Closed += Popup_Closed;

        card.ToolTip = popup;
        ToolTipService.SetInitialShowDelay(card, 150);
        ToolTipService.SetPlacement(card, PlacementMode.Right);
        ToolTipService.SetShowDuration(card, 60000);
        card.ToolTipOpening += (_, _) => popup.Content ??= new CardHoverPopup();
    }

    // ------------------------------------------------------------------ slideshow

    private const int SlideWidth = 254;

    private static readonly TimeSpan SlideTime = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan HoverSettle = TimeSpan.FromMilliseconds(350);

    private static DispatcherTimer? _slideTimer;
    private static DispatcherTimer? _settleTimer;
    private static ModCardViewModel? _hovered;
    private static readonly List<string> Slides = [];
    private static int _slide;

    // Which opening of a popup the running work is for: a popup closed and opened again on the same
    // card must not have the first opening's work carry on beside the second's.
    private static int _generation;

    private static void Popup_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModCardViewModel card }) return;

        StopSlideshow();
        _generation++;
        _hovered = card;
        card.HoverPicture = card.Thumbnail;

        _settleTimer ??= NewTimer(HoverSettle, SettleTimer_Tick);
        _settleTimer.Start();
    }

    private static void Popup_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModCardViewModel card } && ReferenceEquals(card, _hovered))
            StopSlideshow();
    }

    private static async void SettleTimer_Tick(object? sender, EventArgs e)
    {
        _settleTimer?.Stop();
        if (_hovered is not { } card) return;

        var generation = _generation;
        bool StillShowing() => generation == _generation && ReferenceEquals(card, _hovered);

        try
        {
            var pictures = await AppServices.ModPictures.ForAsync(card.Mod);
            if (!StillShowing()) return;

            // Each is fetched and decoded before it may be shown, in order; the show starts as soon
            // as there is a second one to change to.
            foreach (var picture in pictures)
            {
                var loaded = await ThumbnailLoader.PrefetchAsync(picture, SlideWidth);
                if (!StillShowing()) return;
                if (!loaded) continue;

                Slides.Add(picture);
                if (Slides.Count == 1) card.HoverPicture = picture;
                if (Slides.Count == 2)
                {
                    _slideTimer ??= NewTimer(SlideTime, SlideTimer_Tick);
                    _slideTimer.Start();
                }
            }
        }
        catch (Exception ex)
        {
            // A timer tick has nowhere to throw to; the popup keeps the picture it has.
            Core.Services.AppLog.Warn("Workshop", $"hover pictures for {card.Name}: {ex.Message}");
        }
    }

    private static void SlideTimer_Tick(object? sender, EventArgs e)
    {
        if (_hovered is not { } card || Slides.Count < 2)
        {
            _slideTimer?.Stop();
            return;
        }

        _slide = (_slide + 1) % Slides.Count;
        card.HoverPicture = Slides[_slide];
    }

    private static void StopSlideshow()
    {
        _settleTimer?.Stop();
        _slideTimer?.Stop();
        if (_hovered is not null) _hovered.HoverPicture = null;
        _hovered = null;
        _generation++;
        Slides.Clear();
        _slide = 0;
    }

    private static DispatcherTimer NewTimer(TimeSpan interval, EventHandler tick)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += tick;
        return timer;
    }
}
