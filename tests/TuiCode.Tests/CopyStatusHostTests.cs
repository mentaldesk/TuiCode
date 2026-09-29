using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// #318
public class CopyStatusHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task A_copy_says_what_it_copied_and_the_file_path_comes_back_afterwards()
    {
        using var workbench = Workbench();
        using var host = Host(workbench);
        workbench.CopyMessageDuration = TimeSpan.Zero;
        string said = "", after = "";
        var error = true;

        await HostSteps.Run(host,
            () => Open(workbench, host, new TestClipboard()),
            () =>
            {
                Copy(workbench)();
                said = workbench.StatusBar.DisplayedText;
                error = workbench.StatusBar.ShowsError;
            },
            () => workbench.StatusBar.DisplayedText != said,
            () => { after = workbench.StatusBar.DisplayedText; });

        Assert.Equal("Copied 2 lines  •  1,001 characters", said);
        Assert.False(error);
        Assert.Equal(new MockFileSystem().Path.GetFullPath(Path), after);
    }

    [Fact]
    public async Task A_copy_that_did_not_land_says_so_in_the_error_colours_until_the_next_message()
    {
        using var workbench = Workbench();
        using var host = Host(workbench);
        workbench.CopyMessageDuration = TimeSpan.Zero;
        var said = "";
        var error = false;

        await HostSteps.Run(host,
            () => Open(workbench, host, new TestClipboard { Transform = _ => null }),
            Copy(workbench),
            () => { },
            () => { },
            () =>
            {
                said = workbench.StatusBar.DisplayedText;
                error = workbench.StatusBar.ShowsError;
            });

        Assert.Equal("Copy failed: the clipboard didn't take the text (TestClipboard)", said);
        Assert.True(error);
    }

    private const string Path = "/work/a.txt";

    private static void Open(Workbench.Workbench workbench, WorkbenchHost host, TestClipboard clipboard)
    {
        var fs = new MockFileSystem();
        fs.AddFile(Path, new MockFileData($"x\n{new string('y', 1000)}\nz"));
        host.App.Driver!.Clipboard = clipboard;
        workbench.OpenFile(fs.FileInfo.New(Path));
        workbench.Editor.Group.ActiveTab!.TextView.SetCarets([new Caret(new Point(0, 2), new Point(0, 0), Extending: true)]);
    }

    private static Action Copy(Workbench.Workbench workbench) =>
        () => workbench.Editor.Group.ActiveTab!.TextView.InvokeCommand(Command.Copy);

    private static Workbench.Workbench Workbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());

    private static WorkbenchHost Host(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
