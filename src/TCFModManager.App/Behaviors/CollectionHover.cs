using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.Behaviors;

//
// Steam's hover popup on a collection card (Browsing: Collections): title, description, "Contains
// N items" and the first items' pictures with "+N" for the rest. On a card whose DataContext is a
// CollectionCardViewModel: behaviors:CollectionHover.Enabled="True".
//
// The card itself carries only the start of the description and no items, so the list's page is
// read once the pointer has stayed a moment (a pass across the page reads nothing), and kept a
// while for the collection page too (CollectionDetailsCache).
//
public static class CollectionHover
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(CollectionHover), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement card || e.NewValue is not true || card.ToolTip is ToolTip) return;

        // Measured on Steam: #344352, radius 2, a 4px 4px 10px shadow, 5 8 inside, 340px wide,
        // to the right of the card and 5px above its top.
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
        ToolTipService.SetVerticalOffset(card, -5);
        ToolTipService.SetHorizontalOffset(card, 2);
        ToolTipService.SetShowDuration(card, 60000);
        card.ToolTipOpening += (_, _) => popup.Content ??= new CollectionHoverPopup();
    }

    private static readonly TimeSpan HoverSettle = TimeSpan.FromMilliseconds(350);

    private static DispatcherTimer? _settle;
    private static CollectionCardViewModel? _hovered;

    private static void Popup_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CollectionCardViewModel card }) return;

        _hovered = card;
        if (card.HasPreview) return;

        _settle ??= NewTimer();
        _settle.Stop();
        _settle.Start();
    }

    private static void Popup_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CollectionCardViewModel card } && ReferenceEquals(card, _hovered))
        {
            _settle?.Stop();
            _hovered = null;
        }
    }

    private static DispatcherTimer NewTimer()
    {
        var timer = new DispatcherTimer { Interval = HoverSettle };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (_hovered is not { } card) return;

            try
            {
                var details = await AppServices.CollectionDetails.GetAsync(card.Summary.Id, card.Summary.Slug);
                if (details is not null) card.ShowPreview(details);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SpModListsException)
            {
                // The popup keeps what the card knows.
                AppLog.Debug("Collections", $"hover preview of list {card.Summary.Id}: {ex.Message}");
            }
        };
        return timer;
    }
}
