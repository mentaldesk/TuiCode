using TuiCode.Workbench;
using TuiCode.Workbench.Parts;

namespace TuiCode.Tests;

public class WorkbenchHostTests
{
    [Fact]
    public async Task CtrlQ_quits_the_workbench()
    {
        var workbench = new Workbench.Workbench(
            new SidebarPart(),
            new EditorPart(),
            new StatusBarPart());
        using var host = new WorkbenchHost(workbench);

        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = host.RunAsync(cts.Token);

        await runTask;
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out — Ctrl+Q did not stop the loop");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }
}
