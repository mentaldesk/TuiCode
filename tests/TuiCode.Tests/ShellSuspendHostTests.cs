using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Suspend to shell (#463). Boots a TG Application — serialised (#77).
public class ShellSuspendHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private int _stops;

    public ShellSuspendHostTests() => _fs.AddFile("/work/a.txt", new MockFileData("one\n"));

    [Fact]
    public async Task The_sts_mnemonic_suspends()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, jobControl: true);

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => workbench.SubViews.Any(v => v is Workbench.Mnemonics.MnemonicView),
            () => { host.App.InjectKey(new Key('s')); host.App.InjectKey(new Key('t')); host.App.InjectKey(new Key('s')); },
            () => _stops == 1);

        Assert.Equal(1, _stops);
    }

    [Fact]
    public async Task Without_job_control_the_status_bar_says_why_and_nothing_stops()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, jobControl: false, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SuspendToShell),
            () => workbench.StatusBar.DisplayedText.Contains("Can't suspend"));

        Assert.Equal(0, _stops);
        Assert.Contains(ShellSuspend.NoJobControl, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Ctrl_Z_is_still_undo()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, jobControl: true);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(new Key('x')),
            () => workbench.Editor.Group.ActiveTab!.Content.StartsWith('x'),
            () => host.App.InjectKey(Key.Z.WithCtrl),
            () => !workbench.Editor.Group.ActiveTab!.Content.StartsWith('x'));

        Assert.Equal(0, _stops);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, bool jobControl) => BuildHost(workbench, jobControl, out _);

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, bool jobControl, out CommandService commands)
    {
        commands = new CommandService();
        var environment = new FakeEnvironment();
        var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), environment: environment, driverName: DriverRegistry.Names.ANSI);
        host.ShellSuspend = new ShellSuspend(environment, () => jobControl, _ => { }, () => _stops++);
        return host;
    }
}
