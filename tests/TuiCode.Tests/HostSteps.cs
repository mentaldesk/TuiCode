using TuiCode.Workbench;

namespace TuiCode.Tests;

internal static class HostSteps
{
    // Each step runs on its own main-loop iteration (so injected keys are processed in between);
    // a Func<bool> step is polled each iteration until it returns true. Ctrl+Q is sent after the last step.
    public static async Task Run(WorkbenchHost host, params Delegate[] steps)
    {
        var queue = new Queue<Delegate>(steps);
        const int maxIterations = 500;
        var iterations = 0;
        host.App.Iteration += OnIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");
        Assert.Empty(queue);

        void OnIteration(object? sender, EventArgs<IApplication?> e)
        {
            if (queue.Count == 0 || ++iterations > maxIterations)
            {
                host.App.Iteration -= OnIteration;
                host.App.InjectKey(Key.Q.WithCtrl);
                return;
            }
            var done = queue.Peek() switch
            {
                Func<bool> poll => poll(),
                Action act => Execute(act),
                _ => throw new InvalidOperationException("Steps must be Action or Func<bool>"),
            };
            if (done) queue.Dequeue();
        }

        static bool Execute(Action act) { act(); return true; }
    }
}
