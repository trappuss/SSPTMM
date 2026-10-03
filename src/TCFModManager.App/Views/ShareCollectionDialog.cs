using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;

namespace TCFModManager.App.Views;

//
// Fork (SSPTMM, UI tidy-up 5): Share with friends, from a collection's page - the two ways a
// collection goes to friends (see CollectionSharing), on one Steam dialog:
//
//  - Copy share code, with the name it says it is from. Pasted in a chat; sent again after a change.
//  - For your own collection, a shared folder: kept there as a file, rewritten on every change, for
//    friends to follow. For a friend's that you follow, where from - and Stop following.
//
// The buttons inside act straight away and say what they did underneath; Done closes it.
//
public static class ShareCollectionDialog
{
    public static void Show(Guid id)
    {
        if (AppServices.ModLists.Find(id) is not { } list) return;

        var body = new StackPanel { Width = 520 };
        var said = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#A4D007") };

        body.Children.Add(Paragraph(Strings.Sharing_DialogIntro));

        // ---- share code
        body.Children.Add(Heading(Strings.Sharing_CodeHeading));

        TextBox? name = null;
        if (list.IsEditable)
        {
            body.Children.Add(Paragraph(Strings.Sharing_NamePrompt, top: 4));
            name = new TextBox
            {
                Style = (Style)Application.Current.FindResource("SteamSearchField"),
                Height = 34,
                Margin = new Thickness(0, 6, 0, 0),
                Text = CollectionSharing.ShareName ?? "",
            };
            body.Children.Add(name);
        }

        var copy = Button(Strings.Sharing_CopyCode, blue: true);
        copy.Click += (_, _) =>
        {
            if (name is not null) CollectionSharing.ShareName = name.Text;
            if (CollectionSharing.CodeFor(id) is not { } code) return;

            try
            {
                Clipboard.SetText(code);
                said.Foreground = Brush("#A4D007");
                said.Text = LocalizationService.Text(Strings.Sharing_CopiedFormat, code.Length);
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                said.Foreground = Brush("#E05A5A");
                said.Text = LocalizationService.Text(Strings.Sharing_CopyFailedFormat, ex.Message);
            }
        };
        body.Children.Add(copy);

        // ---- shared folder (yours) / followed file (a friend's)
        if (list.IsEditable)
        {
            body.Children.Add(Heading(Strings.Sharing_FolderHeading));
            body.Children.Add(Paragraph(Strings.Sharing_FolderIntro, top: 4));

            var where = Paragraph("", top: 6);
            where.Foreground = Brush("#DCDEDF");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var choose = Button(Strings.Sharing_ChooseFolder, blue: false);
            var stop = Button(Strings.Sharing_StopSharing, blue: false);
            stop.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(choose);
            row.Children.Add(stop);

            void ShowWhere()
            {
                var path = CollectionSharing.SharedFileFor(id);
                where.Text = path is null ? Strings.Sharing_NotInFolder : LocalizationService.Text(Strings.Sharing_KeptInFormat, path);
                stop.Visibility = path is null ? Visibility.Collapsed : Visibility.Visible;
            }

            choose.Click += (_, _) =>
            {
                if (name is not null) CollectionSharing.ShareName = name.Text;
                var dialog = new OpenFolderDialog { Title = Strings.Sharing_ChooseFolderTitle };
                if (dialog.ShowDialog() != true) return;

                var error = CollectionSharing.ShareToFolder(id, dialog.FolderName);
                ShowWhere();
                said.Foreground = Brush(error is null ? "#A4D007" : "#E05A5A");
                said.Text = error is null
                    ? Strings.Sharing_FolderDone
                    : LocalizationService.Text(Strings.Sharing_FolderFailedFormat, error);
            };
            stop.Click += (_, _) =>
            {
                CollectionSharing.StopSharingToFolder(id);
                ShowWhere();
                said.Foreground = Brush("#ACB2B8");
                said.Text = Strings.Sharing_FolderStopped;
            };

            ShowWhere();
            body.Children.Add(where);
            body.Children.Add(row);
        }
        else if (CollectionSharing.FollowedFileFor(id) is { } followed)
        {
            body.Children.Add(Heading(Strings.Sharing_FollowingHeading));
            var where = Paragraph(LocalizationService.Text(Strings.Sharing_FollowingFormat, followed), top: 4);
            var unfollow = Button(Strings.Sharing_StopFollowing, blue: false);
            unfollow.Margin = new Thickness(0, 8, 0, 0);
            unfollow.Click += (_, _) =>
            {
                CollectionSharing.StopFollowing(id);
                unfollow.IsEnabled = false;
                where.Text = Strings.Sharing_Unfollowed;
            };
            body.Children.Add(where);
            body.Children.Add(unfollow);
        }

        body.Children.Add(said);

        SteamDialog.Show(
            LocalizationService.Text(Strings.Sharing_TitleFormat, list.Name),
            body,
            new SteamDialogChoice(Strings.Sharing_Done, SteamDialogButton.Grey, IsDefault: true));

        if (name is not null) CollectionSharing.ShareName = name.Text;
    }

    private static TextBlock Heading(string text) => new()
    {
        Margin = new Thickness(0, 16, 0, 0),
        FontSize = 15,
        Foreground = Brush("#FFFFFF"),
        Text = text,
    };

    private static TextBlock Paragraph(string text, double top = 0) => new()
    {
        Margin = new Thickness(0, top, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("#ACB2B8"),
        Text = text,
    };

    private static Button Button(string text, bool blue) => new()
    {
        Margin = new Thickness(0, 8, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        Style = (Style)Application.Current.FindResource(blue ? "SteamBlueButton" : "SteamButton"),
        Content = text,
    };

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
