using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace TCFModManager.App.Behaviors;

//
// Fork (1.4.9): a list's items drawn a few at once and the rest one at a time, each behind drawing
// and input.
//
// A page of cards was built in one pass: 24 cards took 0.7 s, measured, in which nothing was drawn
// and no click was answered - on every page turn, every change of filter, and each time a page of
// cards was first shown. The cards cost the same to build as before; they now arrive in order,
// the first few with the page and each of the others in its own turn, so the page is there at once
// and stays live while it fills.
//
// Used as an item template that holds only a Border carrying this property: the border is given
// the item, drawn with the real template, when its turn comes. (A Border and not a
// ContentPresenter: one of those written in a template takes its template from the item around
// it, which here is the template it is written in.)
//
public static class StagedContent
{
    // How many are built with the page, before the turns begin. HUNCH: a first row or two.
    private const int AtOnce = 6;

    // And how many a turn. Measured on a page of 24 cards, by the app's own log of how long the
    // window went without answering: one, two, three or six a turn each held it for 55-150 ms a
    // turn, most of that the layout pass a turn costs however many cards are in it - so three,
    // which fills the page three times sooner than one for much the same pause.
    private const int PerTurn = 3;

    public static readonly DependencyProperty TemplateProperty = DependencyProperty.RegisterAttached(
        "Template", typeof(DataTemplate), typeof(StagedContent), new PropertyMetadata(null, OnTemplateChanged));

    public static DataTemplate? GetTemplate(DependencyObject element) => (DataTemplate?)element.GetValue(TemplateProperty);

    public static void SetTemplate(DependencyObject element, DataTemplate? value) => element.SetValue(TemplateProperty, value);

    private static readonly Queue<Decorator> Waiting = new();

    // How many of the ones asked for since the app was last idle were built at once.
    private static int _atOnce;

    private static bool _stepQueued;
    private static bool _resetQueued;

    private static void OnTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Decorator presenter) return;

        if (e.NewValue is not DataTemplate)
        {
            presenter.Child = null;
            return;
        }

        if (_atOnce < AtOnce && Waiting.Count == 0)
        {
            _atOnce++;
            Fill(presenter);
            QueueReset(presenter.Dispatcher);
            return;
        }

        Waiting.Enqueue(presenter);
        QueueStep(presenter.Dispatcher);
    }

    private static void Fill(Decorator host)
    {
        if (GetTemplate(host) is not { } template) return;
        if (host.Child is ContentPresenter { } made && made.ContentTemplate == template) return;

        var presenter = new ContentPresenter { ContentTemplate = template };
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding());
        host.Child = presenter;
    }

    private static void QueueStep(Dispatcher dispatcher)
    {
        if (_stepQueued) return;

        _stepQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _stepQueued = false;

            // One that left the page while it waited costs nothing: on to the next in the same turn.
            var made = 0;
            while (Waiting.TryDequeue(out var next))
            {
                if (!next.IsLoaded && PresentationSource.FromVisual(next) is null) continue;

                Fill(next);
                if (++made >= PerTurn) break;
            }

            if (Waiting.Count > 0) QueueStep(dispatcher);
        });
    }

    // Once everything asked for has been built and nothing else is waiting, the next list starts
    // with its first few at once again.
    private static void QueueReset(Dispatcher dispatcher)
    {
        if (_resetQueued) return;

        _resetQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            _resetQueued = false;
            if (Waiting.Count > 0) QueueReset(dispatcher);
            else _atOnce = 0;
        });
    }
}
