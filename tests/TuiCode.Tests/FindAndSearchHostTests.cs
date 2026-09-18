using Terminal.Gui.ViewBase;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Key-driven tests for find/replace and workspace search (#33). Boots a TG Application — serialised (#77).
public class FindAndSearchHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public async Task CtrlF_finds_as_you_type_Enter_steps_on_and_Esc_closes_the_bar()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("foo\nfoo\nfoo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;
        var findBarFocusedAfterCtrlF = false;
        var afterTyping = (Row: -1, Selected: "");
        var rowAfterEnter = -1;
        var statusWhileFinding = "";
        var closedCleanly = false;

        await HostSteps.Run(host,
            // OpenFile (not Editor.Open) so the status bar carries its normal message to revert to.
            () => { workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")); tab = workbench.Editor.Group.ActiveTab; },
            () => { tab!.FocusContent(); host.App.InjectKey(Key.F.WithCtrl); },
            () =>
            {
                findBarFocusedAfterCtrlF = workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault()?.HasFocus == true;
                foreach (var c in "foo") host.App.InjectKey(new Key(c));
            },
            () =>
            {
                afterTyping = (tab!.CursorRow, tab.SelectedText);
                statusWhileFinding = workbench.StatusBar.DisplayedText;
                host.App.InjectKey(Key.Enter);
            },
            () =>
            {
                rowAfterEnter = tab!.CursorRow;
                host.App.InjectKey(Key.Esc);
            },
            () =>
            {
                closedCleanly = !workbench.SubViewsDeep().OfType<FindBarView>().Any()
                                && tab!.ContentHasFocus
                                && tab.SelectedText.Length == 0
                                && workbench.StatusBar.DisplayedText == tab.File.FullName;
            });

        Assert.True(findBarFocusedAfterCtrlF, "Ctrl+F should show and focus the find bar");
        Assert.Equal((0, "foo"), afterTyping);
        Assert.Equal(1, rowAfterEnter);
        Assert.Equal("Enter next match · Shift+Enter previous match · Esc close", statusWhileFinding);
        Assert.True(closedCleanly, "Esc should remove the bar, clear the selection, refocus the editor and restore the status bar");
    }

    // The find bar's scope is layered, not modal: workbench shortcuts keep working while it has focus.
    [Fact]
    public async Task Workbench_shortcuts_still_fire_while_the_find_bar_has_focus()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("foo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var saved = false;
        workbench.Editor.FileSaved += (_, _) => saved = true;

        await HostSteps.Run(host,
            () => { workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt")).FocusContent(); },
            () => host.App.InjectKey(Key.F.WithCtrl),
            () => host.App.InjectKey(Key.S.WithCtrl));

        Assert.True(saved, "Ctrl+S should save while the find bar is focused");
    }

    [Fact]
    public async Task CtrlH_replaces_the_current_match_with_Enter()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("cat cat\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;
        var statusInReplaceField = "";

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt")); },
            () => { tab!.FocusContent(); host.App.InjectKey(Key.H.WithCtrl); },
            () => { foreach (var c in "cat") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Tab),
            () =>
            {
                statusInReplaceField = workbench.StatusBar.DisplayedText;
                foreach (var c in "dog") host.App.InjectKey(new Key(c));
            },
            () => host.App.InjectKey(Key.Enter));

        Assert.Equal("dog cat", tab!.Lines[0]);
        Assert.Equal("Enter replace · Ctrl+Enter replace all · Tab find field · Esc close", statusInReplaceField);
    }

    [Fact]
    public async Task CtrlEnter_replaces_all_matches_from_the_find_field()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("cat cat\ncat\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt")); },
            () => { tab!.FocusContent(); host.App.InjectKey(Key.H.WithCtrl); },
            () => { foreach (var c in "cat") host.App.InjectKey(new Key(c)); },
            () => { workbench.SubViewsDeep().OfType<FindBarView>().Single().Replacement = "dog"; },
            () => host.App.InjectKey(Key.Enter.WithCtrl));

        Assert.Equal(["dog dog", "dog", ""], tab!.Lines);
    }

    // #33: a sidebar item's shortcut shows its tab (revealing the sidebar), and pressed again while that
    // tab is already showing, hides the sidebar.
    [Fact]
    public async Task Sidebar_item_shortcuts_show_their_tab_then_hide_the_sidebar_when_pressed_again()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var sidebar = workbench.Sidebar;
        var states = new List<(bool Visible, SidebarTab Tab)>();
        var searchQueryFocused = false;

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () =>
            {
                states.Add((workbench.IsSidebarVisible, sidebar.ActiveTab));
                searchQueryFocused = sidebar.Search.InputsHaveFocus;
                host.App.InjectKey(Key.F.WithCtrl.WithShift);
            },
            () =>
            {
                states.Add((workbench.IsSidebarVisible, sidebar.ActiveTab));
                host.App.InjectKey(Key.E.WithCtrl.WithShift);
            },
            () =>
            {
                states.Add((workbench.IsSidebarVisible, sidebar.ActiveTab));
                host.App.InjectKey(Key.E.WithCtrl.WithShift);
            },
            () => states.Add((workbench.IsSidebarVisible, sidebar.ActiveTab)));

        Assert.Equal(
            [(true, SidebarTab.Find), (false, SidebarTab.Find), (true, SidebarTab.Explorer), (false, SidebarTab.Explorer)],
            states);
        Assert.True(searchQueryFocused, "Showing the search tab should focus its query input");
    }

    [Fact]
    public async Task Search_panel_Enter_moves_to_results_and_opens_the_selected_match()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("nothing\nsome needle\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var search = workbench.Sidebar.Search;

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => { foreach (var c in "needle") host.App.InjectKey(new Key(c)); },
            // The search runs in the background once the app is live; wait for it to land.
            () => search.Result.MatchCount == 1,
            () => host.App.InjectKey(Key.Enter),   // query → results (first match pre-selected)
            () => host.App.InjectKey(Key.Enter));  // open it

        var tab = workbench.Editor.Group.ActiveTab;
        Assert.NotNull(tab);
        Assert.Equal("a.txt", tab.File.Name);
        Assert.Equal("needle", tab.SelectedText);
        Assert.Equal(1, tab.CursorRow);
    }

    [Fact]
    public async Task The_status_bar_follows_the_cursor_to_the_next_match()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("foo\nbar\n  foo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedPosition == "Ln 1, Col 1",
            () => host.App.InjectKey(Key.F.WithCtrl),
            () => { foreach (var c in "foo") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Enter),
            () => workbench.StatusBar.DisplayedPosition == "Ln 3, Col 6",
            () => host.App.InjectKey(Key.Esc));
    }

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

internal static class ViewTreeExtensions
{
    public static IEnumerable<View> SubViewsDeep(this View view) =>
        view.SubViews.SelectMany(v => v.SubViewsDeep().Prepend(v));
}
