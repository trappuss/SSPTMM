using System.Collections.Concurrent;

namespace TCFModManager.App.Behaviors;

//
// One background thread that decodes every picture the app shows, one after another.
//
// Decoding moved off the UI thread so a page of thirty thumbnails stops stuttering the scroll.
// It runs on a single single-threaded-apartment thread rather than the thread pool because WPF's
// imaging components are COM objects written for STA callers; one thread taking them in turn is
// the arrangement they expect, and it is still off the UI thread.
//
internal static class DecodeThread
{
    private static readonly BlockingCollection<Action> Work = new();

    static DecodeThread()
    {
        var thread = new Thread(() =>
        {
            foreach (var job in Work.GetConsumingEnumerable()) job();
        })
        {
            IsBackground = true,
            Name = nameof(DecodeThread),
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static Task<T> Run<T>(Func<T> decode)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        Work.Add(() =>
        {
            try
            {
                done.SetResult(decode());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });

        return done.Task;
    }
}
