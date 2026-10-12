using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Close other tabs and Close all tabs (#518). Boots a TG Application — serialised (#77).
public class CloseTabsHostTests : StaticConfigurationTest
{
    private const string Question = "Save them before closing?";

    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;
    private readonly WorkspaceStateStore _store;

    public CloseTabsHostTests()
    {
        _keybindings = new KeybindingService(_commands);
        _store = new WorkspaceStateStore(_fs, "/state.json");
        _fs.AddDirectory("/work");
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
            _fs.AddFile($"/work/{name}", new MockFileData("one\n"));
    }

    [Fact]
    public void Both_follow_close_active_editor_in_the_file_menu_with_mnemonics_and_no_key()
    {
        using var workbench = BuildWorkbench();
        using var _ = BuildHost(workbench);

        var file = CommandMenu.Layout.Single(m => m.Title == "_File").Ids;
        var close = Array.IndexOf(file, CommandIds.CloseActiveEditor);
        Assert.Equal([CommandIds.CloseOtherTabs, CommandIds.CloseAllTabs], file[(close + 1)..(close + 3)]);
        Assert.Equal("cot", CommandMnemonics.For(CommandIds.CloseOtherTabs));
        Assert.Equal("cat", CommandMnemonics.For(CommandIds.CloseAllTabs));
        Assert.DoesNotContain(_keybindings.Bindings, b => b.CommandId is CommandIds.CloseOtherTabs or CommandIds.CloseAllTabs);
        Assert.Equal(["Close all tabs", "Close other tabs"],
            _commands.Registered.Where(c => c.Id is CommandIds.CloseOtherTabs or CommandIds.CloseAllTabs).Select(c => c.Label).Order());
    }

    [Fact]
    public async Task Close_other_tabs_leaves_the_active_tab_and_remembers_only_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); Open(workbench, "c.txt"); },
            () => _commands.TryExecute(CommandIds.CloseOtherTabs));

        Assert.Equal(["c.txt"], Names(workbench.Editor.Group));
        Assert.Equal([Full("/work/c.txt")], _store.Load(Full("/work"))!.Files);
    }

    [Fact]
    public async Task A_key_bound_to_close_all_tabs_runs_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); },
            () => _keybindings.Bind("Ctrl+K", CommandIds.CloseAllTabs),
            () => host.App.InjectKey(Key.K.WithCtrl),
            () => workbench.Editor.Group.Value is null);
    }

    [Fact]
    public async Task Close_other_tabs_leaves_the_other_group_alone()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); Open(workbench, "c.txt"); },
            () => _commands.TryExecute(CommandIds.MoveToOtherGroup),
            () => groups.IsSplit,
            () => _commands.TryExecute(CommandIds.FocusOtherGroup),
            () => ReferenceEquals(workbench.Editor.Group, groups.First),
            () => _commands.TryExecute(CommandIds.CloseOtherTabs));

        Assert.Equal(["b.txt"], Names(groups.First));
        Assert.Equal(["c.txt"], Names(groups.Second));
        Assert.Equal([Full("/work/c.txt")], _store.Load(Full("/work"))!.Second!.Files);
    }

    [Fact]
    public async Task Close_other_tabs_from_a_diff_keeps_its_file_too()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "a.txt", edit: "mine\n");
                Open(workbench, "b.txt");
                group.CompareToSaved(group.Tabs.Single(t => t.File.Name == "a.txt"));
            },
            () => _commands.TryExecute(CommandIds.CloseOtherTabs));

        Assert.Null(Confirm(workbench));
        Assert.IsType<DiffTab>(group.Value);
        Assert.Equal(["a.txt"], Names(group));
        Assert.Equal(2, group.TabCollection.Count());
    }

    [Fact]
    public async Task Close_other_tabs_asks_only_about_the_dirty_tabs_it_closes()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";
        string[] buttons = [];

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "a.txt", edit: "mine a\n");
                Open(workbench, "b.txt");
                Open(workbench, "c.txt", edit: "mine c\n");
            },
            () => _commands.TryExecute(CommandIds.CloseOtherTabs),
            () => Confirm(workbench) is not null,
            () =>
            {
                var view = Confirm(workbench)!;
                asked = view.SubViews.OfType<Label>().First().Text;
                buttons = [.. view.SubViews.OfType<Button>().Select(b => b.Text)];
            });

        Assert.Equal(WorkbenchHost.UnsavedChanges(["a.txt"], Question), asked.ReplaceLineEndings("\n"));
        Assert.Equal(["Save all", "Don't save", "Cancel"], buttons);
    }

    [Fact]
    public async Task Esc_closes_nothing_and_leaves_every_tab_dirty()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt", edit: "mine a\n"); Open(workbench, "b.txt", edit: "mine b\n"); },
            () => _commands.TryExecute(CommandIds.MoveToOtherGroup),
            () => groups.IsSplit,
            () => _commands.TryExecute(CommandIds.CloseAllTabs),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => Confirm(workbench) is null);

        Assert.Equal(["a.txt"], Names(groups.First));
        Assert.Equal(["b.txt"], Names(groups.Second));
        Assert.All(groups.Tabs, t => Assert.True(t.IsDirty));
        Assert.Equal("one\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task Save_all_writes_the_tabs_being_closed_and_closes_them()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt", edit: "mine a\n"); Open(workbench, "b.txt", edit: "mine b\n"); },
            () => _commands.TryExecute(CommandIds.CloseOtherTabs),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save all"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Equal("mine a\n", _fs.File.ReadAllText("/work/a.txt"));
        Assert.Equal("one\n", _fs.File.ReadAllText("/work/b.txt"));
        Assert.Equal(["b.txt"], Names(group));
        Assert.True(group.ActiveTab!.IsDirty);
    }

    [Fact]
    public async Task Dont_save_closes_every_tab_and_leaves_the_files_alone()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt", edit: "mine a\n"); Open(workbench, "b.txt"); },
            () => _commands.TryExecute(CommandIds.MoveToOtherGroup),
            () => groups.IsSplit,
            () => _commands.TryExecute(CommandIds.CloseAllTabs),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Don't save"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Empty(groups.First.TabCollection);
        Assert.False(groups.IsSplit);
        Assert.Equal("one\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task Close_all_tabs_with_nothing_dirty_closes_both_groups_without_asking()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var groups = workbench.Editor.Groups;
        var asked = false;
        workbench.SubViewAdded += (_, e) => asked |= e.SubView is ConfirmView;

        await HostSteps.Run(host,
            () => { Open(workbench, "a.txt"); Open(workbench, "b.txt"); Open(workbench, "c.txt"); },
            () => _commands.TryExecute(CommandIds.MoveToOtherGroup),
            () => groups.IsSplit,
            () => _commands.TryExecute(CommandIds.CloseAllTabs));

        Assert.False(asked);
        Assert.Empty(groups.First.TabCollection);
        Assert.Empty(groups.Second.TabCollection);
        Assert.False(groups.IsSplit);
        var state = _store.Load(Full("/work"))!;
        Assert.Empty(state.Files);
        Assert.Null(state.Second);
    }

    // Cancel starts focused, so Tab round the row until the wanted button has it.
    private static Func<bool> Reach(WorkbenchHost host, Workbench.Workbench workbench, string label) => () =>
    {
        if (Confirm(workbench)?.FocusedChoice == label) return true;
        host.App.InjectKey(Key.Tab);
        return false;
    };

    private static ConfirmView? Confirm(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ConfirmView>().SingleOrDefault();

    private static IEnumerable<string> Names(EditorGroup group) => group.Tabs.Select(t => t.File.Name).Order();

    private void Open(Workbench.Workbench workbench, string name, string? edit = null)
    {
        workbench.OpenFile(_fs.FileInfo.New($"/work/{name}"));
        if (edit is not null) workbench.Editor.Group.ActiveTab!.Content = edit;
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
