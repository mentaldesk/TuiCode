using Terminal.Gui.Drivers;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// #711
public class StatusBarUnderscoreHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task A_message_keeps_every_underscore_on_screen()
    {
        using var workbench = Workbench();
        using var host = Host(workbench);
        var row = "";

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            () => { workbench.StatusBar.ShowMessage("Renamed _bar to _count"); },
            () => { },
            () => { row = StatusRow(host, workbench); });

        Assert.Contains("Renamed _bar to _count", row);
    }

    [Fact]
    public async Task A_file_path_keeps_every_underscore_on_screen()
    {
        using var workbench = Workbench();
        using var host = Host(workbench);
        var fs = new MockFileSystem();
        var path = fs.Path.GetFullPath("/my_app/src/order_service.cs");
        fs.AddFile(path, new MockFileData("class A {}\n"));
        var row = "";

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            () => workbench.OpenFile(fs.FileInfo.New(path)),
            () => { },
            () => { row = StatusRow(host, workbench); });

        Assert.Contains(path, row);
    }

    private static string StatusRow(WorkbenchHost host, Workbench.Workbench workbench)
    {
        var y = workbench.StatusBar.FrameToScreen().Y;
        var contents = host.App.Driver!.Contents!;
        return string.Concat(Enumerable.Range(0, contents.GetLength(1)).Select(x => contents[y, x].Grapheme));
    }

    private static Workbench.Workbench Workbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());

    private static WorkbenchHost Host(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
