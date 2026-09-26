using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XamlAnimatedGif;

namespace TCFModManager.App.Behaviors;

//
// Plays an animated GIF in an Image - while any of it is on screen, and only then.
//
// XamlAnimatedGif draws the frames. What this adds is the pausing: an animated GIF keeps drawing
// whether or not any of it can be seen, and one GIF scrolled out of view still kept the build
// machine's processor about an eighth busy with the page otherwise still. So it is paused while
// none of it can be seen - scrolled out of every scroll area it sits in, or on a page, tab or popup
// that is hidden - and carries on from the same frame the moment any of it comes back. Nothing that
// can be seen changes: every GIF plays whenever it is in view, as sp-mod.com shows it.
//
// Used by the description pictures (RemotePicture) and every thumbnail (ThumbnailLoader): a mod's
// cover on a card, in the hover popup and Quick View, and the item page's gallery strip.
//
internal static class GifPlayback
{
    private static readonly DependencyProperty WatcherProperty = DependencyProperty.RegisterAttached(
        "Watcher", typeof(Watcher), typeof(GifPlayback), new PropertyMetadata(null));

    //
    // <paramref name="restoreStill"/> puts back whatever still the image should show when it is
    // not playing. Needed because XamlAnimatedGif, once it has played in an Image, keeps handlers on
    // it that clear the image's Source every time it is unloaded and loaded again - even after its
    // stream is taken away (read in its InitAnimation / Image_Unloaded, 2.3.2). A still shown there
    // afterwards (a hover popup's slide that was a GIF, then is not, and the popup reopens) would
    // go blank. So after each load, once the library's own handler has run, a still that is missing
    // is asked for again.
    //
    /// <summary>Plays <paramref name="gif"/> in <paramref name="image"/>, from its first frame.</summary>
    public static void Play(Image image, byte[] gif, Action<Image> restoreStill)
    {
        AnimationBehavior.SetSourceStream(image, new MemoryStream(gif, writable: false));

        if (image.GetValue(WatcherProperty) is not Watcher watcher)
        {
            watcher = new Watcher(image);
            image.SetValue(WatcherProperty, watcher);
        }

        watcher.Restore = restoreStill;
        watcher.Start();
    }

    /// <summary>Stops a GIF this image was playing, if it was; nothing otherwise.</summary>
    public static void Stop(Image image)
    {
        if (image.GetValue(WatcherProperty) is not Watcher watcher || !watcher.IsPlaying) return;

        watcher.Stop();
        AnimationBehavior.SetSourceStream(image, null);
    }

    public static bool IsPlaying(Image image) => image.GetValue(WatcherProperty) is Watcher { IsPlaying: true };

    // One per Image that has played a GIF: the scroll areas it sits in, and the handlers.
    private sealed class Watcher
    {
        private readonly Image _image;
        private readonly List<ScrollViewer> _viewers = [];
        private bool _hooked;

        public bool IsPlaying { get; private set; }

        public Action<Image>? Restore { get; set; }

        public Watcher(Image image) => _image = image;

        public void Start()
        {
            IsPlaying = true;

            if (!_hooked)
            {
                _hooked = true;
                _image.Loaded += (_, _) =>
                {
                    Rewatch();
                    if (!IsPlaying) RestoreStillLater();
                };
                _image.Unloaded += (_, _) =>
                {
                    Unwatch();
                    Update();
                };
                _image.IsVisibleChanged += (_, _) => Update();

                // Once more after the library has finished loading: it starts a GIF playing itself.
                AnimationBehavior.AddLoadedHandler(_image, (_, _) =>
                {
                    Update();
                    _image.Dispatcher.BeginInvoke(Update, System.Windows.Threading.DispatcherPriority.Loaded);
                });
            }

            if (_image.IsLoaded) Rewatch();
        }

        public void Stop()
        {
            IsPlaying = false;
            Unwatch();
        }

        // After the library's own Loaded handler, which runs in the same pass and clears Source.
        private void RestoreStillLater() =>
            _image.Dispatcher.BeginInvoke(() =>
            {
                if (!IsPlaying && _image.Source is null) Restore?.Invoke(_image);
            }, System.Windows.Threading.DispatcherPriority.Loaded);

        // Every scroll area the image sits in, however deep.
        private void Rewatch()
        {
            Unwatch();
            if (!IsPlaying) return;

            for (var parent = VisualTreeHelper.GetParent(_image); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not ScrollViewer viewer) continue;
                viewer.ScrollChanged += Viewer_ScrollChanged;
                _viewers.Add(viewer);
            }

            Update();
        }

        private void Unwatch()
        {
            foreach (var viewer in _viewers) viewer.ScrollChanged -= Viewer_ScrollChanged;
            _viewers.Clear();
        }

        // Scrolled, resized, or the content above it changed height: all of them raise this.
        private void Viewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => Update();

        private void Update()
        {
            if (!IsPlaying || AnimationBehavior.GetAnimator(_image) is not { } animator) return;

            if (_image.IsVisible && InView())
            {
                if (animator.IsPaused && !animator.IsComplete) animator.Play();
            }
            else if (!animator.IsPaused)
            {
                animator.Pause();
            }
        }

        private bool InView()
        {
            if (!_image.IsLoaded) return false;

            var bounds = new Rect(_image.RenderSize);
            foreach (var viewer in _viewers)
            {
                try
                {
                    var where = _image.TransformToAncestor(viewer).TransformBounds(bounds);
                    if (!where.IntersectsWith(new Rect(0, 0, viewer.ActualWidth, viewer.ActualHeight))) return false;
                }
                catch (InvalidOperationException)
                {
                    // No longer inside that scroll area; when in doubt it plays.
                }
            }

            return true;
        }
    }
}
