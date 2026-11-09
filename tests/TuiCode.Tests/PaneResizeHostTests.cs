using Terminal.Gui.Views;
using MentalDesk.Tui.Layout;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Mnemonics;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Widen, narrow and reset whichever side pane has the keys (#667). Boots a TG Application — serialised (#77).
public class PaneResizeHostTests : StaticConfigurationTest
{
    private const int WideTerminal = 200;

    private static readonly string[] PaneCommands = [CommandIds.WidenPane, CommandIds.NarrowPane, CommandIds.ResetPaneWidth];
    private static readonly string[] PaneMnemonics = ["wp", "np", "rp"];

    private readonly MockFileSystem _fs = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly FakeGitCli _git = new();

    public PaneResizeHostTests()
    {
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.FileCommits[_fs.Path.GetFullPath("/work/a.txt")] =
            [new GitFileCommit(new string('1', 40), "Ada", DateTimeOffset.UnixEpoch, "Add a", "a.txt", null)];
    }

    public static TheoryData<string, string> SidebarTabs => new()
    {
        { CommandIds.ShowExplorer, "Explorer" },
        { CommandIds.FindGlobally, "Find" },
        { CommandIds.FocusUsages, "Usages" },
        { CommandIds.FocusReview, "Review" },
    };

    [Theory]
    [MemberData(nameof(SidebarTabs))]
    public async Task In_any_sidebar_tab_the_commands_resize_the_sidebar_and_leave_AI_assistance_alone(string tab, string word)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var widths = new List<int>();

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => workbench.ShowAssistant(true),
            () => commands.TryExecute(tab),
            () => workbench.StatusBar.DisplayedFocus == word,
            () => commands.TryExecute(CommandIds.WidenPane),
            SidebarAt(workbench, 35),
            () => commands.TryExecute(CommandIds.NarrowPane),
            () => commands.TryExecute(CommandIds.NarrowPane),
            SidebarAt(workbench, 25),
            () => widths.Add(workbench.AssistantSidebar.Frame.Width),
            () => commands.TryExecute(CommandIds.ResetPaneWidth),
            SidebarAt(workbench, PaneWidths.SidebarDefault));

        Assert.Equal([PaneWidths.AssistantDefault], widths);
    }

    [Fact]
    public async Task From_the_question_box_and_the_answer_the_commands_resize_AI_assistance_and_leave_the_sidebar_alone()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => workbench.ShowAssistant(true),
            () => workbench.Assistant.FocusInput(),
            () => workbench.StatusBar.DisplayedFocus == "AI assistance",
            () => commands.TryExecute(CommandIds.WidenPane),
            AssistantAt(workbench, 55),
            () => workbench.Assistant.Chat.SetFocus(),
            () => workbench.Assistant.Chat.HasFocus && !workbench.Assistant.InputHasFocus,
            () => commands.TryExecute(CommandIds.NarrowPane),
            () => commands.TryExecute(CommandIds.NarrowPane),
            AssistantAt(workbench, 45),
            () => commands.TryExecute(CommandIds.ResetPaneWidth),
            AssistantAt(workbench, PaneWidths.AssistantDefault));

        Assert.Equal(PaneWidths.SidebarDefault, workbench.Sidebar.Frame.Width);
    }

    [Fact]
    public async Task On_120_columns_with_the_sidebar_at_30_AI_assistance_stops_at_50_and_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, Size(host, workbench, 120),
            () => workbench.ShowAssistant(true),
            () => workbench.Assistant.FocusInput(),
            () => workbench.StatusBar.DisplayedFocus == "AI assistance",
            AssistantAt(workbench, 50),
            () => commands.TryExecute(CommandIds.WidenPane),
            () => workbench.StatusBar.DisplayedText == "AI assistance is as wide as it can be");

        Assert.Equal(50, workbench.AssistantSidebar.Frame.Width);
        Assert.Equal(SidePanes.MinimumMainWidth, workbench.Editor.Frame.Width);
    }

    [Fact]
    public async Task At_15_columns_each_pane_says_it_is_as_narrow_as_it_can_be()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var said = new List<string>();

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => commands.TryExecute(CommandIds.ShowExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => { for (var i = 0; i < 4; i++) commands.TryExecute(CommandIds.NarrowPane); },
            SidebarAt(workbench, SidePanes.MinimumWidth),
            () => said.Add(workbench.StatusBar.DisplayedText),
            () => workbench.ShowAssistant(true),
            () => workbench.Assistant.FocusInput(),
            () => workbench.StatusBar.DisplayedFocus == "AI assistance",
            () => { for (var i = 0; i < 8; i++) commands.TryExecute(CommandIds.NarrowPane); },
            AssistantAt(workbench, SidePanes.MinimumWidth),
            () => said.Add(workbench.StatusBar.DisplayedText));

        Assert.Equal(["Sidebar is as narrow as it can be", "AI assistance is as narrow as it can be"], said);
    }

    [Fact]
    public async Task A_step_away_from_a_limit_says_nothing_about_the_width()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => commands.TryExecute(CommandIds.ShowExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => commands.TryExecute(CommandIds.WidenPane),
            SidebarAt(workbench, 35));

        Assert.DoesNotContain("35", workbench.StatusBar.DisplayedText);
        Assert.DoesNotContain("as wide", workbench.StatusBar.DisplayedText);
    }

    public static TheoryData<string> NotSidePanes => new() { "Editor", "Diff", "History" };

    [Theory]
    [MemberData(nameof(NotSidePanes))]
    public async Task Outside_a_side_pane_they_are_not_offered_and_do_nothing(string place)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> leader = [];
        IReadOnlyList<string> palette = [];
        var dimmed = false;

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => workbench.OpenFile(_fs.FileInfo.New(_fs.Path.GetFullPath("/work/a.txt"))),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\nTWO\n"; if (place == "Diff") commands.TryExecute(CommandIds.CompareToSaved); },
            () => place != "Diff" || workbench.StatusBar.DisplayedFocus == "Diff",
            () => { if (place == "History") commands.TryExecute(CommandIds.ToggleFileHistory); },
            () => place != "History" || workbench.StatusBar.DisplayedFocus == "History",
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => { leader = Leader(workbench)!.Mnemonics; host.App.InjectKey(Key.Esc); },
            () => Leader(workbench) is null,
            () => commands.TryExecute(CommandIds.ShowActions),
            () => Palette(workbench) is not null,
            () => { palette = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null,
            () => { dimmed = PaneCommands.All(id => !commands.IsEnabled(id)); },
            () => { foreach (var id in PaneCommands) commands.TryExecute(id); });

        Assert.All(PaneMnemonics, m => Assert.DoesNotContain(m, leader));
        Assert.DoesNotContain("Widen pane", palette);
        Assert.DoesNotContain("Narrow pane", palette);
        Assert.DoesNotContain("Reset pane width", palette);
        Assert.True(dimmed);
        Assert.Equal(PaneWidths.SidebarDefault, workbench.Sidebar.Frame.Width);
        Assert.Equal(0, _settings.SaveCount);
    }

    [Fact]
    public async Task In_a_side_pane_the_leader_and_the_palette_offer_them_and_the_leader_runs_them()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> leader = [];
        IReadOnlyList<string> palette = [];

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => commands.TryExecute(CommandIds.ShowExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => commands.TryExecute(CommandIds.ShowActions),
            () => Palette(workbench) is not null,
            () => { palette = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => { leader = Leader(workbench)!.Mnemonics; host.App.InjectKey(Key.W); },
            () => host.App.InjectKey(Key.P),
            SidebarAt(workbench, 35),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => host.App.InjectKey(Key.R),
            () => host.App.InjectKey(Key.P),
            SidebarAt(workbench, PaneWidths.SidebarDefault));

        Assert.All(PaneMnemonics, m => Assert.Contains(m, leader));
        Assert.Contains("Widen pane", palette);
        Assert.Contains("Narrow pane", palette);
        Assert.Contains("Reset pane width", palette);
        Assert.DoesNotContain("ws", leader);
        Assert.DoesNotContain("ns", leader);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_View_menu_dims_them_unless_the_keys_are_in_a_side_pane(bool inSidePane)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var dimmed = new List<bool>();

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => workbench.OpenFile(_fs.FileInfo.New(_fs.Path.GetFullPath("/work/a.txt"))),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { if (inSidePane) commands.TryExecute(CommandIds.ShowExplorer); },
            () => workbench.StatusBar.DisplayedFocus == (inSidePane ? "Explorer" : "Editor"),
            () => host.App.InjectKey(Key.F10),
            () => workbench.MenuBar.IsOpen(),
            () => dimmed.AddRange(ViewItems(workbench).Where(i => i.Title.Replace("_", "") is "Widen pane" or "Narrow pane" or "Reset pane width").Select(i => i.Dimmed)),
            () => host.App.InjectKey(Key.Esc),
            () => !workbench.MenuBar.IsOpen());

        Assert.Equal([!inSidePane, !inSidePane, !inSidePane], dimmed);
    }

    [Fact]
    public void None_has_a_default_key_and_all_are_in_Keyboard_Shortcuts()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI);

        Assert.DoesNotContain(keybindings.Bindings, b => PaneCommands.Contains(b.CommandId));
        var rows = KeybindingRows.Build(commands.Registered, keybindings.Bindings.ToArray(), "pane")
            .Where(r => PaneCommands.Contains(r.CommandId))
            .ToArray();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, r => Assert.Equal(KeybindingRows.Unbound, r.Keys));
    }

    [Fact]
    public async Task A_key_saved_against_the_old_widen_sidebar_id_widens_the_pane_you_are_in()
    {
        Assert.Equal("workbench.action.widenSidebar", CommandIds.WidenPane);
        Assert.Equal("workbench.action.narrowSidebar", CommandIds.NarrowPane);
        _settings.SetKeybindingOverrides([
            new KeybindingOverride(TestKeys.Chord("F8"), "workbench.action.widenSidebar"),
            new KeybindingOverride(TestKeys.Chord("F7"), "workbench.action.narrowSidebar"),
        ]);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => workbench.ShowAssistant(true),
            () => workbench.Assistant.FocusInput(),
            () => workbench.StatusBar.DisplayedFocus == "AI assistance",
            () => host.App.InjectKey(Key.F8),
            AssistantAt(workbench, 55),
            () => host.App.InjectKey(Key.F7),
            () => host.App.InjectKey(Key.F7),
            AssistantAt(workbench, 45));

        Assert.Equal(PaneWidths.SidebarDefault, workbench.Sidebar.Frame.Width);
    }

    [Fact]
    public async Task The_widest_pane_gives_way_a_column_at_a_time_and_the_editor_keeps_40()
    {
        _settings.SidebarWidth = 30;
        _settings.AssistantWidth = 50;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var seen = new List<(int, int, int)>();

        await HostSteps.Run(host, Size(host, workbench, 120),
            () => workbench.ShowAssistant(true),
            () => workbench.AssistantSidebar.Frame.Width > 0,
            Note(workbench, seen),
            Size(host, workbench, 110),
            Note(workbench, seen),
            Size(host, workbench, 90),
            Note(workbench, seen),
            Size(host, workbench, 70),
            Note(workbench, seen),
            Size(host, workbench, 120),
            Note(workbench, seen));

        Assert.Equal([(30, 40, 50), (30, 40, 40), (25, 40, 25), (15, 40, 15), (30, 40, 50)], seen);
        Assert.Equal(30, _settings.SidebarWidth);
        Assert.Equal(50, _settings.AssistantWidth);
    }

    [Fact]
    public async Task A_width_survives_hiding_and_showing_its_pane_and_comes_back_after_a_restart()
    {
        var widths = new List<int>();
        using (var workbench = BuildWorkbench())
        using (var host = BuildHost(workbench, out var commands))
        {
            await HostSteps.Run(host, Size(host, workbench, WideTerminal),
                () => commands.TryExecute(CommandIds.ShowExplorer),
                () => workbench.StatusBar.DisplayedFocus == "Explorer",
                () => commands.TryExecute(CommandIds.WidenPane),
                SidebarAt(workbench, 35),
                () => commands.TryExecute(CommandIds.ToggleSidebar),
                () => !workbench.Sidebar.Visible && workbench.Editor.Frame.X == 0,
                () => commands.TryExecute(CommandIds.ToggleSidebar),
                SidebarAt(workbench, 35),
                () => widths.Add(workbench.Editor.Frame.X));
        }
        Assert.Equal([35], widths);
        Assert.Equal(35, _settings.SidebarWidth);

        using var again = BuildWorkbench();
        using var second = BuildHost(again, out _);
        await HostSteps.Run(second, Size(second, again, WideTerminal),
            SidebarAt(again, 35));
    }

    [Fact]
    public async Task Resetting_a_pane_writes_the_default_and_leaves_the_other_panes_width()
    {
        _settings.SidebarWidth = 45;
        _settings.AssistantWidth = 70;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, Size(host, workbench, WideTerminal),
            () => commands.TryExecute(CommandIds.ShowExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => commands.TryExecute(CommandIds.ResetPaneWidth),
            SidebarAt(workbench, PaneWidths.SidebarDefault));

        Assert.Equal(PaneWidths.SidebarDefault, _settings.SidebarWidth);
        Assert.Equal(70, _settings.AssistantWidth);
    }

    private static IEnumerable<CommandMenuItem> ViewItems(Workbench.Workbench workbench) =>
        workbench.MenuBar.SubViews.OfType<MenuBarItem>().Single(m => m.Title.Replace("_", "") == "View")
            .PopoverMenu!.Root!.SubViews.OfType<CommandMenuItem>();

    private static Func<bool> SidebarAt(Workbench.Workbench workbench, int width) => () => workbench.Sidebar.Frame.Width == width;

    private static Func<bool> AssistantAt(Workbench.Workbench workbench, int width) => () => workbench.AssistantSidebar.Frame.Width == width;

    private static Action Note(Workbench.Workbench workbench, List<(int, int, int)> seen) =>
        () => seen.Add((workbench.Sidebar.Frame.Width, workbench.Editor.Frame.Width, workbench.AssistantSidebar.Frame.Width));

    private static Func<bool> Size(WorkbenchHost host, Workbench.Workbench workbench, int width) =>
        () =>
        {
            HostSteps.PinScreenSize(host, width, 24);
            return workbench.Viewport.Width == width;
        };

    private static MnemonicView? Leader(Workbench.Workbench workbench) => workbench.SubViews.OfType<MnemonicView>().FirstOrDefault();

    private static ActionView? Palette(Workbench.Workbench workbench) => workbench.SubViews.OfType<ActionView>().FirstOrDefault();

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI, fileSystem: _fs, git: _git);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New(_fs.Path.GetFullPath("/work")));
        return workbench;
    }
}
