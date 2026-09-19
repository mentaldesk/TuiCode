using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// The status bar's selection count (#153). Boots a TG Application — serialised (#77).
public class SelectionCountHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public async Task Selecting_text_shows_its_count_and_a_click_clears_it()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha bravo\ncharlie\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        List<string> shown = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.Editor.Group.ActiveTab!.MoveCursor(0, 6),
            () => host.App.InjectKey(Key.End.WithShift),
            () => shown.Add(workbench.StatusBar.DisplayedPosition),
            () => Click(TextView(workbench), new Point(2, 1)),
            () => shown.Add(workbench.StatusBar.DisplayedPosition));

        Assert.Equal(["Ln 1, Col 12 (5 selected)", "Ln 2, Col 3"], shown);
    }

    [Fact]
    public async Task Several_carets_show_their_total_and_Esc_goes_back_to_one()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\nbravo\ncharlie\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        List<string> shown = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => host.App.InjectKey(Key.CursorDown.WithCtrl.WithAlt),
            () => host.App.InjectKey(Key.CursorDown.WithCtrl.WithAlt),
            () => shown.Add(workbench.StatusBar.DisplayedPosition),
            () => host.App.InjectKey(Key.CursorRight.WithShift),
            () => host.App.InjectKey(Key.CursorRight.WithShift),
            () => shown.Add(workbench.StatusBar.DisplayedPosition),
            () => host.App.InjectKey(Key.Esc),
            () => shown.Add(workbench.StatusBar.DisplayedPosition));

        Assert.Equal(["3 selections", "3 selections (6 selected)", "Ln 1, Col 3 (2 selected)"], shown);
    }

    [Fact]
    public async Task Switching_tabs_shows_the_new_tabs_selection()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("bravo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        List<string> shown = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.Editor.Group.ActiveTab!.Select(new TextMatch(0, 0, 3)),
            () => workbench.OpenFile(_fs.FileInfo.New("/work/b.txt")),
            () => shown.Add(workbench.StatusBar.DisplayedPosition),
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => shown.Add(workbench.StatusBar.DisplayedPosition));

        Assert.Equal(["Ln 1, Col 1", "Ln 1, Col 4 (3 selected)"], shown);
    }

    private static void Click(EditorTextView view, Point at)
    {
        foreach (var flags in new[] { MouseFlags.LeftButtonPressed, MouseFlags.LeftButtonReleased, MouseFlags.LeftButtonClicked })
            view.NewMouseEvent(new Mouse { Flags = flags, Position = at });
    }

    private static EditorTextView TextView(Workbench.Workbench workbench) =>
        workbench.Editor.Group.ActiveTab!.SubViews.OfType<EditorTextView>().Single();

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
