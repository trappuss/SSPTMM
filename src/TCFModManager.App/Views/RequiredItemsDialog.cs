using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TCFModManager.App.Localization;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace TCFModManager.App.Views;

/// <summary>What Subscribe should queue after the required-items question.</summary>
public enum RequiredItemsChoice
{
    Cancel,
    All,
    JustThisItem,
}

//
// Steam's "Additional Required Items" dialog, asked when Subscribe finds that a mod needs others
// this install does not have (sharedfiles_functions_logged_in.js, SubscribeItem):
//
//   Additional Required Items
//   <text, the list of required items>
//   [Subscribe to Just This Item] [Subscribe to All] [Cancel]
//
// The button order is Steam's. One deliberate difference: Enter chooses Subscribe to All, where
// Steam's Enter chooses Just This Item. A Workshop game downloads what an item needs anyway; SPT
// does not, and a mod installed without them fails to load - so the key that answers without
// reading gives the working install.
//
// The first sentence is Steam's own. The other two are written for this app: a click here opens
// the item's sp-mod.com page (which also counts for the read-the-page gate that follows), and the
// list already holds everything the items need in turn, where Steam warns that it might.
//
public static class RequiredItemsDialog
{
    public static RequiredItemsChoice Ask(string modName, IReadOnlyList<ModPageLink> required)
    {
        var body = new StackPanel { MaxWidth = 660 };
        body.Children.Add(Sentence(LocalizationService.Text(Strings.Subscribe_RequiredIntroFormat, modName), 0));
        body.Children.Add(Sentence(Strings.Subscribe_RequiredClickNote, 21));
        body.Children.Add(Sentence(Strings.Subscribe_RequiredAllNote, 21));

        var rows = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        foreach (var link in required) rows.Children.Add(Row(link));

        // A long list scrolls rather than pushing the buttons off the screen.
        body.Children.Add(new ScrollViewer
        {
            Content = rows,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        var answer = SteamDialog.Show(
            Strings.Subscribe_RequiredTitle,
            body,
            new SteamDialogChoice(Strings.Subscribe_JustThisItem, SteamDialogButton.Green),
            new SteamDialogChoice(Strings.Subscribe_All, SteamDialogButton.Blue, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        return answer switch
        {
            0 => RequiredItemsChoice.JustThisItem,
            1 => RequiredItemsChoice.All,
            _ => RequiredItemsChoice.Cancel,
        };
    }

    private static TextBlock Sentence(string text, double above) => new()
    {
        Text = text,
        Margin = new Thickness(0, above, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 21,
    };

    // The item's name, and a tick once its page has been opened from here.
    private static Button Row(ModPageLink link)
    {
        var tick = new SymbolIcon
        {
            Symbol = SymbolRegular.Checkmark16,
            FontSize = 16,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
        };
        tick.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(ModPageLink.IsOpened))
        {
            Source = link,
            Converter = new BooleanToVisibilityConverter(),
        });
        DockPanel.SetDock(tick, Dock.Right);

        var name = new TextBlock
        {
            Text = link.Name,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        // A row with no page never gets the tick (a link without a page counts as read from the
        // start), and rows take no keyboard focus, so Enter always answers the dialog.
        var content = new DockPanel();
        if (link.HasUrl) content.Children.Add(tick);
        content.Children.Add(name);

        var row = new Button
        {
            Style = (Style)Application.Current.FindResource("SteamModalRow"),
            Content = content,
            IsEnabled = link.HasUrl,
            Focusable = false,
            ToolTip = link.Url,
        };
        row.Click += (_, _) => Open(Window.GetWindow(row), link);
        return row;
    }

    // Inside the app when it can show the page, in the browser when it cannot - as the gate does.
    private static void Open(Window? owner, ModPageLink link)
    {
        if (!link.HasUrl || ModPageWindow.Show(owner, [link])) return;

        Process.Start(new ProcessStartInfo(link.Url!) { UseShellExecute = true });
        link.IsOpened = true;
    }
}
