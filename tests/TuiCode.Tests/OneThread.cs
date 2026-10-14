using System.Collections.Concurrent;

namespace TuiCode.Tests;

/// <summary>Runs an async test on one thread, as the app runs editors on the UI thread: an editor's document throws on any other.</summary>
internal static class OneThread
{
    public static Task Run(Func<Task> body)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            var context = new Context();
            SynchronizationContext.SetSynchronizationContext(context);
            context.Post(async _ =>
            {
                try
                {
                    await body();
                    done.SetResult();
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
                finally
                {
                    context.Complete();
                }
            }, null);
            context.Pump();
        }) { IsBackground = true };
        thread.Start();
        return done.Task;
    }

    private sealed class Context : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                // The test has finished; nothing is left to run this.
            }
        }

        public void Complete() => _queue.CompleteAdding();

        public void Pump()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                callback(state);
        }
    }
}
