using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class WorkbenchHostTests
{
    [Fact]
    public async Task CtrlQ_quits_the_workbench()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings);

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

    private static Workbench.Workbench BuildWorkbench() =>
        new(
            new SidebarPart(new FileExplorerView()),
            new EditorPart(),
            new StatusBarPart());
}
