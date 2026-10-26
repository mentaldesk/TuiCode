using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// The AI assistance sidebar's width and where it's remembered (#619). Boots a TG Application — serialised (#77).
public class AssistantSidebarHostTests : StaticConfigurationTest
{
    private const int WideTerminal = 160;
    private const int Rows = 24;
    private const int BorderRow = 5;

    // Opened at 50 columns against the right edge, so its left border is the first column it occupies.
    private const int BorderColumn = WideTerminal - AssistantSizing.Default;

    private readonly MockFileSystem _fs = new();
    private readonly InMemorySettingsService _settings = new();

    [Fact]
    public async Task A_drag_on_its_left_border_follows_the_pointer_and_releasing_saves_and_reports_the_width()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var widths = new List<int>();

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Open(workbench),
            Press(host, BorderColumn),
            Move(host, 100),
            () => widths.Add(workbench.DrawnAssistantWidth),
            Move(host, 95),
            () => widths.Add(workbench.DrawnAssistantWidth),
            () => Assert.Equal(0, _settings.SaveCount),
            Release(host, 95));

        Assert.Equal([60, 65], widths);
        Assert.Equal(65, workbench.DrawnAssistantWidth);
        Assert.Equal(65, _settings.AssistantWidth);
        Assert.Equal(1, _settings.SaveCount);
        Assert.Equal("AI assistance width: 65", workbench.StatusBar.DisplayedText);
        Assert.Equal(WideTerminal - 65, workbench.Editor.Frame.Right);
    }

    [Fact]
    public async Task Dragging_past_its_floor_stops_at_thirty_columns()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Open(workbench),
            Press(host, BorderColumn),
            Release(host, WideTerminal - 1));

        Assert.Equal(AssistantSizing.Min, workbench.DrawnAssistantWidth);
        Assert.Equal("AI assistance width: 30 (minimum)", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Dragging_past_the_editors_floor_stops_at_the_maximum()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Open(workbench),
            Press(host, BorderColumn),
            Release(host, 0));

        Assert.Equal(WideTerminal - SidebarSizing.Default - SidebarSizing.EditorFloor, workbench.DrawnAssistantWidth);
        Assert.Equal(SidebarSizing.EditorFloor, workbench.Editor.Frame.Width);
        Assert.Equal("AI assistance width: 90 (maximum — the editor needs 40 columns)", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task With_it_hidden_a_press_where_its_border_was_resizes_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Open(workbench),
            () => workbench.ShowAssistant(false),
            Press(host, BorderColumn),
            Release(host, 100));

        Assert.Equal(0, _settings.SaveCount);
        Assert.Null(_settings.AssistantWidth);
    }

    [Fact]
    public async Task A_narrower_terminal_narrows_it_first_then_the_left_sidebar_and_a_wider_one_brings_both_back()
    {
        _settings.SidebarWidth = 40;
        _settings.AssistantWidth = 60;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var widths = new List<(int, int)>();

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Open(workbench),
            () => widths.Add(Widths(workbench)),
            Size(host, workbench, 110),
            () => widths.Add(Widths(workbench)),
            Size(host, workbench, 90),
            () => widths.Add(Widths(workbench)),
            Size(host, workbench, WideTerminal),
            () => widths.Add(Widths(workbench)));

        Assert.Equal([(40, 60), (40, 30), (20, 30), (40, 60)], widths);
        Assert.Equal(60, workbench.AssistantWidth);
    }

    [Fact]
    public async Task It_first_opens_at_forty_percent_of_a_terminal_too_narrow_for_fifty_columns()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, 120),
            Open(workbench));

        Assert.Equal(48, workbench.DrawnAssistantWidth);
    }

    [Fact]
    public async Task It_opens_at_the_width_it_was_dragged_to()
    {
        _settings.AssistantWidth = 70;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            Open(workbench));

        Assert.Equal(70, workbench.DrawnAssistantWidth);
    }

    [Fact]
    public async Task Whether_it_was_open_is_kept_with_the_folder()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddDirectory("/other");
        var store = new WorkspaceStateStore(_fs, "/state.json");
        store.Save(Full("/work"), new WorkspaceState([], null) { Assistant = true });
        using var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), store);
        using var host = BuildHost(workbench);
        var restored = false;

        await HostSteps.Run(host,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/work"))),
            () => { restored = workbench.IsAssistantShowing; },
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/other"))),
            () => !workbench.IsAssistantShowing,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/work"))),
            () => workbench.IsAssistantShowing,
            () => workbench.ShowAssistant(false));

        Assert.True(restored);
        Assert.False(store.Load(Full("/work"))!.Assistant);
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private static (int, int) Widths(Workbench.Workbench workbench) => (workbench.DrawnSidebarWidth, workbench.DrawnAssistantWidth);

    // The border the user grabs is laid out a layout after the width changes.
    private static Func<bool> Open(Workbench.Workbench workbench) =>
        () =>
        {
            workbench.ShowAssistant(true);
            return workbench.AssistantSidebar.FrameToScreen() is var frame
                && frame.Width == workbench.DrawnAssistantWidth && frame.Right == workbench.Viewport.Width;
        };

    private static Action Press(WorkbenchHost host, int column) =>
        Inject(host, MouseFlags.LeftButtonPressed, column);

    private static Action Move(WorkbenchHost host, int column) =>
        Inject(host, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport, column);

    private static Action Release(WorkbenchHost host, int column) =>
        Inject(host, MouseFlags.LeftButtonReleased, column);

    private static Action Inject(WorkbenchHost host, MouseFlags flags, int column) =>
        () => host.App.InjectMouse(new Mouse { Flags = flags, ScreenPosition = new Point(column, BorderRow) });

    private static Func<bool> Size(WorkbenchHost host, Workbench.Workbench workbench, int width) =>
        () =>
        {
            HostSteps.PinScreenSize(host, width, Rows);
            return workbench.Viewport.Width == width && workbench.AssistantSidebar.FrameToScreen().Width == workbench.DrawnAssistantWidth;
        };

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI, fileSystem: _fs);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }
}
