using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// Dragging the sidebar's border to resize it (#252). Boots a TG Application — serialised (#77).
public class SidebarDragHostTests : StaticConfigurationTest
{
    private const int WideTerminal = 120;
    private const int Rows = 24;

    // The sidebar starts at column 0, so its right border is the last column it occupies.
    private const int BorderColumn = SidebarSizing.Default - 1;
    private const int BorderRow = 5;

    private readonly MockFileSystem _fs = new();
    private readonly InMemorySettingsService _settings = new();

    [Fact]
    public async Task A_drag_on_the_border_follows_the_pointer()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var widths = new List<int>();

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Press(host, BorderColumn),
            Move(host, 39),
            () => widths.Add(workbench.DrawnSidebarWidth),
            Move(host, 44),
            () => widths.Add(workbench.DrawnSidebarWidth),
            Release(host, 44),
            () => widths.Add(workbench.DrawnSidebarWidth));

        Assert.Equal([40, 45, 45], widths);
    }

    [Fact]
    public async Task Releasing_saves_the_width_and_reports_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Press(host, BorderColumn),
            Move(host, 44),
            Release(host, 44));

        Assert.Equal(45, _settings.SidebarWidth);
        Assert.Equal(1, _settings.SaveCount);
        Assert.Equal("Sidebar width: 45", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task A_drag_that_never_releases_is_not_saved()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Press(host, BorderColumn),
            Move(host, 44));

        Assert.Equal(45, workbench.DrawnSidebarWidth);
        Assert.Equal(SidebarSizing.Default, _settings.SidebarWidth);
        Assert.Equal(0, _settings.SaveCount);
    }

    [Fact]
    public async Task Dragging_past_the_editors_floor_stops_at_the_maximum()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Press(host, BorderColumn),
            Release(host, WideTerminal - 1));

        Assert.Equal(WideTerminal - SidebarSizing.EditorFloor, workbench.DrawnSidebarWidth);
        Assert.Equal("Sidebar width: 80 (maximum — the editor needs 40 columns)", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Dragging_past_the_sidebars_floor_stops_at_the_minimum()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Press(host, BorderColumn),
            Release(host, 0));

        Assert.Equal(SidebarSizing.Min, workbench.DrawnSidebarWidth);
        Assert.Equal("Sidebar width: 15 (minimum)", workbench.StatusBar.DisplayedText);
    }

    // On a terminal too narrow for both floors the sidebar's wins, so the drag can't move it at all.
    [Fact]
    public async Task On_a_terminal_too_narrow_for_both_floors_the_sidebar_stops_at_its_own()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, 50),
            BorderSettledAt(workbench, SidebarSizing.Min),
            Press(host, SidebarSizing.Min - 1),
            Release(host, 49));

        Assert.Equal(SidebarSizing.Min, workbench.DrawnSidebarWidth);
        Assert.Equal("Sidebar width: 15 (minimum)", workbench.StatusBar.DisplayedText);
    }

    [Theory]
    [InlineData(5)] // inside the explorer tree
    [InlineData(SidebarSizing.Default + 10)] // in the editor
    public async Task A_press_that_misses_the_border_resizes_nothing(int column)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Press(host, column),
            Move(host, 60),
            Release(host, 60));

        Assert.Equal(SidebarSizing.Default, workbench.DrawnSidebarWidth);
        Assert.Equal(0, _settings.SaveCount);
    }

    [Fact]
    public async Task With_the_sidebar_hidden_a_press_where_the_border_was_does_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => { commands.TryExecute(CommandIds.ToggleSidebar); },
            Press(host, BorderColumn),
            Release(host, 60));

        Assert.False(workbench.IsSidebarVisible);
        Assert.Equal(SidebarSizing.Default, workbench.DrawnSidebarWidth);
        Assert.Equal(0, _settings.SaveCount);
    }

    [Fact]
    public async Task A_press_where_a_modal_covers_the_border_resizes_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var covered = false;

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => { if (Key.TryParse("Ctrl+,", out var open)) host.App.InjectKey(open); },
            () => { covered = workbench.SubViews.OfType<SettingsView>().Any(); },
            Press(host, BorderColumn),
            Release(host, 60),
            () => host.App.InjectKey(Key.Esc));

        Assert.True(covered, "the settings overlay didn't open");
        Assert.Equal(SidebarSizing.Default, workbench.DrawnSidebarWidth);
        Assert.Equal(0, _settings.SaveCount);
    }

    [Fact]
    public async Task A_drag_leaves_the_editors_cursor_and_focus_alone()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha bravo\ncharlie delta\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var focused = false;

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.Editor.Group.ActiveTab!.MoveCursor(1, 4),
            Press(host, BorderColumn),
            Move(host, 60),
            Release(host, 60),
            () => { focused = TextView(workbench).HasFocus; });

        Assert.True(focused);
        Assert.Equal((1, 4), (workbench.Editor.Group.ActiveTab!.CursorRow, workbench.Editor.Group.ActiveTab!.CursorColumn));
        Assert.Equal(61, workbench.DrawnSidebarWidth);
    }

    [Fact]
    public async Task A_drag_leaves_the_explorers_selection_and_focus_alone()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("bravo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        string? selected = null;
        var focused = false;

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => { commands.TryExecute(CommandIds.FocusSidebar); },
            () => host.App.InjectKey(Key.CursorDown),
            () => { selected = workbench.Sidebar.Explorer.SelectedObject?.FullName; },
            Press(host, BorderColumn),
            Move(host, 44),
            Release(host, 44),
            () => { focused = workbench.Sidebar.Explorer.HasFocus; });

        Assert.True(focused);
        Assert.NotNull(selected);
        Assert.Equal(selected, workbench.Sidebar.Explorer.SelectedObject?.FullName);
        Assert.Equal(45, workbench.DrawnSidebarWidth);
    }

    private static Action Press(WorkbenchHost host, int column) =>
        Inject(host, MouseFlags.LeftButtonPressed, column);

    private static Action Move(WorkbenchHost host, int column) =>
        Inject(host, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport, column);

    private static Action Release(WorkbenchHost host, int column) =>
        Inject(host, MouseFlags.LeftButtonReleased, column);

    private static Action Inject(WorkbenchHost host, MouseFlags flags, int column) =>
        () => host.App.InjectMouse(new Mouse { Flags = flags, ScreenPosition = new Point(column, BorderRow) });

    // A resize reaches the sidebar on the layout it triggers, and its Frame — the border the user
    // grabs — on the one after, so waiting on the width it lands at covers both.
    private static Func<bool> BorderSettledAt(Workbench.Workbench workbench, int width) =>
        () => workbench.DrawnSidebarWidth == width && workbench.Sidebar.FrameToScreen().Width == width;

    // Resizing the terminal takes effect on the next layout, and a driver may report a size of its
    // own once more after startup, so it's re-applied until the workbench is laid out at it.
    private static Func<bool> Size(WorkbenchHost host, Workbench.Workbench workbench, int width) =>
        () =>
        {
            host.App.Driver!.SetScreenSize(width, Rows);
            return workbench.Viewport.Width == width;
        };

    private static EditorTextView TextView(Workbench.Workbench workbench) =>
        workbench.Editor.Group.ActiveTab!.SubViews.OfType<EditorTextView>().Single();

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) => BuildHost(workbench, out _);

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }
}
