using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using TCFModManager.App.Services;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Markup;

namespace TCFModManager.App.Views;

public partial class MediaViewerOverlay : UserControl
{
    private MediaViewerViewModel ViewModel => AppServices.MediaViewer;

    private WebView2? _player;

    public MediaViewerOverlay()
    {
        InitializeComponent();

        Visibility = Visibility.Collapsed;
        ViewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaViewerViewModel.IsOpen):
                Visibility = ViewModel.IsOpen ? Visibility.Visible : Visibility.Collapsed;
                if (ViewModel.IsOpen) Focus();
                else StopVideo();
                break;

            case nameof(MediaViewerViewModel.Current):
                _ = ShowCurrentAsync();
                break;
        }
    }

    private async Task ShowCurrentAsync()
    {
        var media = ViewModel.Current;

        // A video stops as soon as the viewer moves off it.
        if (media?.Kind != MarkupMediaKind.Video) StopVideo();

        if (media is null) return;

        if (media.Kind == MarkupMediaKind.Image)
        {
            Picture.Url = media.Url;
            return;
        }

        Picture.Url = null;

        if (_player is null && WebViews.IsAvailable)
        {
            _player = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
            VideoHost.Children.Add(_player);
        }

        var ready = _player is not null && await WebViews.InitializeAsync(_player);

        // Starting the web view the first time takes a moment, in which the viewer may have been
        // closed or moved on - and then this video must not start playing behind it.
        if (!ViewModel.IsOpen || !ReferenceEquals(ViewModel.Current, media)) return;

        if (ready && media.EmbedUrl is { } embed)
        {
            VideoFallback.Visibility = Visibility.Collapsed;
            _player!.Visibility = Visibility.Visible;
            _player.CoreWebView2.Navigate(embed);
            return;
        }

        // No web view: the thumbnail, and the button that opens the video on YouTube.
        if (_player is not null) _player.Visibility = Visibility.Collapsed;
        VideoThumbnail.Url = media.Thumbnail;
        VideoFallback.Visibility = Visibility.Visible;
    }

    private void StopVideo()
    {
        // about:blank rather than hiding it: a hidden player keeps playing its sound.
        if (_player?.CoreWebView2 is { } core) core.Navigate("about:blank");
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ViewModel.CloseCommand.Execute(null);
                break;
            case Key.Left when ViewModel.PreviousCommand.CanExecute(null):
                ViewModel.PreviousCommand.Execute(null);
                break;
            case Key.Right when ViewModel.NextCommand.CanExecute(null):
                ViewModel.NextCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // A click on the picture itself does nothing; anywhere else on the scrim closes the viewer.
    private void Content_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Scrim_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Grid) ViewModel.CloseCommand.Execute(null);
    }
}
