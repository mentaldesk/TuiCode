using System.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Two editor groups side by side (#460). Boots a TG Application — serialised (#77).
public class EditorGroupsHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;
    private readonly WorkspaceStateStore _store;

    public EditorGroupsHostTests()
    {
        _keybindings = new KeybindingService(_commands);
        _store = new WorkspaceStateStore(_fs, "/state.json");
        _fs.AddDirectory("/work");
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
            _fs.AddFile($"/work/{name}", new MockFileData(name + "\n"));
    }

    [Fact]
    public void The_group_commands_have_their_keys_mnemonics_and_place_in_the_view_menu()
    {
        using var workbench = BuildWorkbench();
        using var _ = BuildHost(workbench);

        Assert.Equal("Ctrl+\\", Binding(CommandIds.MoveToOtherGroup));
        Assert.Equal("F6", Binding(CommandIds.FocusOtherGroup));
        Assert.Equal("mg", CommandMnemonics.For(CommandIds.MoveToOtherGroup));
        Assert.Equal("fo", CommandMnemonics.For(CommandIds.FocusOtherGroup));
        Assert.Equal("jg", CommandMnemonics.For(CommandIds.JoinGroups));
        var view = CommandMenu.Layout.Single(m => m.Title == "_View").Ids;
        Assert.Contains(CommandIds.MoveToOtherGroup, view);
        Assert.Contains(CommandIds.FocusOtherGroup, view);
        Assert.Contains(CommandIds.JoinGroups, view);
        Assert.Equal(["Focus other group", "Join groups", "Move to other group"],
            _commands.Registered.Where(c => c.Id is CommandIds.MoveToOtherGroup or CommandIds.FocusOtherGroup or CommandIds.JoinGroups)
                .Select(c => c.Label).Order());
    }

    [Fact]
    public async Task Ctrl_backslash_puts_the_active_tab_in_a_second_group_beside_the_first()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var editor = workbench.Editor;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); },
            () => host.App.InjectKey(CtrlBackslash),
            () => editor.Groups.IsSplit && editor.Frames[1].Frame.Width > 0);

        Assert.Same(editor.Groups.Second, editor.Group);
        Assert.Equal("b.txt", editor.Groups.Second.ActiveTab?.File.Name);
        Assert.Equal("a.txt", editor.Groups.First.ActiveTab?.File.Name);
        Assert.True(editor.Groups.Second.ActiveTab!.ContentHasFocus);
        Assert.InRange(editor.Frames[0].Frame.Width - editor.Frames[1].Frame.Width, -1, 1);
        Assert.EndsWith("b.txt", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task Moving_the_only_tab_with_no_split_says_so_and_changes_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt"); },
            () => _commands.TryExecute(CommandIds.MoveToOtherGroup));

        Assert.Equal("Open another file to split with", workbench.StatusBar.Message);
        Assert.False(workbench.Editor.Groups.IsSplit);
    }

    [Fact]
    public async Task F6_moves_the_keys_to_the_other_groups_tab_and_typing_goes_there()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); },
            () => host.App.InjectKey(CtrlBackslash),
            () => groups.IsSplit,
            () => host.App.InjectKey(Key.F6),
            () => groups.First.ActiveTab!.ContentHasFocus,
            () => host.App.InjectKey(new Key('X')),
            () => groups.First.ActiveTab!.Content.StartsWith('X'));

        Assert.Same(groups.First, workbench.Editor.Group);
        Assert.Equal("b.txt\n", groups.Second.ActiveTab!.Content.ReplaceLineEndings("\n"));
        Assert.EndsWith("a.txt", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task Clicking_in_a_group_focuses_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); },
            () => host.App.InjectKey(CtrlBackslash),
            () => groups.IsSplit && workbench.Editor.Frames[1].Frame.Width > 0,
            () => Click(host, workbench.Editor.Frames[0].FrameToScreen().Location + new Size(5, 5)),
            () => ReferenceEquals(workbench.Editor.Group, groups.First),
            () => _commands.TryExecute(CommandIds.CloseActiveEditor));

        Assert.False(groups.IsSplit);
        Assert.Equal(["b.txt"], groups.First.Tabs.Select(t => t.File.Name));
    }

    [Fact]
    public async Task Closing_the_focused_groups_last_tab_hands_the_keys_to_the_other()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); },
            () => host.App.InjectKey(CtrlBackslash),
            () => groups.IsSplit,
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => !groups.IsSplit,
            () => _commands.TryExecute(CommandIds.SaveActiveEditor));

        Assert.Same(groups.First, workbench.Editor.Group);
        Assert.Equal("a.txt", workbench.Editor.Group.ActiveTab?.File.Name);
    }

    [Fact]
    public async Task Opening_a_file_from_the_explorer_that_is_open_in_the_other_group_focuses_it_there()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); },
            () => host.App.InjectKey(CtrlBackslash),
            () => groups.IsSplit,
            () => { Open(workbench, "a.txt"); },
            () => groups.First.ActiveTab!.ContentHasFocus);

        Assert.Same(groups.First, workbench.Editor.Group);
        Assert.Single(groups.Tabs, t => t.File.Name == "a.txt");
    }

    [Fact]
    public async Task Join_groups_puts_every_tab_back_in_one()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); },
            () => host.App.InjectKey(CtrlBackslash),
            () => groups.IsSplit,
            () => { Open(workbench, "c.txt"); },
            () => _commands.TryExecute(CommandIds.JoinGroups));

        Assert.False(groups.IsSplit);
        Assert.Equal(3, groups.First.Tabs.Count);
        Assert.Equal("c.txt", workbench.Editor.Group.ActiveTab?.File.Name);
    }

    [Fact]
    public async Task Save_all_and_find_in_files_cover_both_groups()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "a.txt").Content = "mine a\n";
                Open(workbench, "b.txt").Content = "mine b\n";
            },
            () => host.App.InjectKey(CtrlBackslash),
            () => groups.IsSplit,
            () => Assert.Equal(["/work/a.txt", "/work/b.txt"],
                workbench.Sidebar.Search.OpenBuffers!.Snapshot().Keys.Select(p => p.Replace('\\', '/').Replace("C:", "")).Order()),
            () => _commands.TryExecute(CommandIds.SaveAll),
            () => workbench.StatusBar.Message == "Saved 2 files");

        Assert.Equal("mine a\n", _fs.File.ReadAllText("/work/a.txt"));
        Assert.Equal("mine b\n", _fs.File.ReadAllText("/work/b.txt"));
    }

    [Fact]
    public async Task A_diff_tab_moves_to_the_other_group_like_any_tab()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt").Content = "mine\n"; },
            () => _commands.TryExecute(CommandIds.CompareToSaved),
            () => groups.First.ActiveDiffTab is not null,
            () => host.App.InjectKey(CtrlBackslash),
            () => groups.IsSplit);

        Assert.NotNull(groups.Second.ActiveDiffTab);
        Assert.Equal("a.txt", groups.First.ActiveTab?.File.Name);
    }

    [Fact]
    public async Task Back_jumps_into_the_group_that_holds_the_file()
    {
        _fs.AddFile("/work/long.txt", new MockFileData(string.Concat(Enumerable.Range(1, 100).Select(n => $"line {n}\n"))));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => { Open(workbench, "long.txt"); },
            () => { Open(workbench, "b.txt"); },
            () => host.App.InjectKey(CtrlBackslash),
            () => groups.IsSplit,
            () => _commands.TryExecute(CommandIds.NavigateBack),
            () => ReferenceEquals(workbench.Editor.Group, groups.First));

        Assert.Equal("long.txt", workbench.Editor.Group.ActiveTab?.File.Name);
        Assert.Single(groups.Tabs, t => t.File.Name == "long.txt");
    }

    [Fact]
    public async Task Reopening_the_folder_brings_back_both_groups_and_which_was_focused()
    {
        using (var workbench = BuildWorkbench())
        using (var host = BuildHost(workbench))
        {
            await HostSteps.Run(host,
                () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); Open(workbench, "c.txt"); },
                () => host.App.InjectKey(CtrlBackslash),
                () => workbench.Editor.Groups.IsSplit);
        }

        Assert.Contains("Second", _fs.File.ReadAllText("/state.json"));
        using var reopened = BuildWorkbench();
        var commands = new CommandService();
        using var again = new WorkbenchHost(reopened, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
        var groups = reopened.Editor.Groups;

        await HostSteps.Run(again, () => { });

        Assert.True(groups.IsSplit);
        Assert.Equal(["a.txt", "b.txt"], groups.First.Tabs.Select(t => t.File.Name).Order());
        Assert.Equal("b.txt", groups.First.ActiveTab?.File.Name);
        Assert.Equal(["c.txt"], groups.Second.Tabs.Select(t => t.File.Name));
        Assert.Same(groups.Second, reopened.Editor.Group);
    }

    [Fact]
    public void A_folder_saved_before_groups_opens_as_one()
    {
        _store.Save(Full("/work"), new WorkspaceState([Full("/work/a.txt"), Full("/work/b.txt")], Full("/work/a.txt")));

        using var workbench = BuildWorkbench();

        Assert.False(workbench.Editor.Groups.IsSplit);
        Assert.Equal(2, workbench.Editor.Groups.First.Tabs.Count);
        Assert.Equal("a.txt", workbench.Editor.Group.ActiveTab?.File.Name);
    }

    private static readonly Key CtrlBackslash = new Key('\\').WithCtrl;

    private string? Binding(string id) => _keybindings.Bindings.SingleOrDefault(b => b.CommandId == id)?.Display;

    private static void Click(WorkbenchHost host, Point at)
    {
        host.App.InjectMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, ScreenPosition = at });
        host.App.InjectMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, ScreenPosition = at });
    }

    private TuiCode.Editor.EditorTab Open(Workbench.Workbench workbench, string name)
    {
        workbench.OpenFile(_fs.FileInfo.New($"/work/{name}"));
        return workbench.Editor.Group.ActiveTab!;
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), _store);
        workbench.OpenFolder(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, _keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
}
