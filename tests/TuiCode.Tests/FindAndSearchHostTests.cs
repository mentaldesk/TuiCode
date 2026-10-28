using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Search;
using TuiCode.Workbench;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Help;
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
        Assert.Equal(tab!.File.FullName, statusWhileFinding);
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

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt")); },
            () => { tab!.FocusContent(); host.App.InjectKey(Key.H.WithCtrl); },
            () => { foreach (var c in "cat") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Tab),
            () => { foreach (var c in "dog") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Enter));

        Assert.Equal("dog cat", tab!.Lines[0]);
    }

    [Fact]
    public async Task F1_in_the_find_field_lists_the_find_bars_keys_and_Esc_hands_the_field_back()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("foo\nfoo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        FindBarView? bar = null;
        KeysDialog? help = null;
        var status = "";
        var queryAfterHelp = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { workbench.Editor.Group.ActiveTab!.FocusContent(); host.App.InjectKey(Key.F.WithCtrl); },
            () => (bar = workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault()) is { HasFocus: true },
            () => { foreach (var c in "foo") host.App.InjectKey(new Key(c)); },
            () => bar!.Query == "foo",
            () => { status = workbench.StatusBar.DisplayedText; host.App.InjectKey(Key.F1); },
            () => (help = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => !workbench.SubViews.OfType<KeysDialog>().Any(),
            () => { queryAfterHelp = bar!.Query; host.App.InjectKey(new Key('x')); },
            () => bar!.Query == "x");

        Assert.Equal(_fs.Path.GetFullPath("/work/a.txt"), status);
        Assert.Equal("foo", queryAfterHelp);
        Assert.Equal("Find bar", help!.Sheet.Here!.Heading);
        Assert.Equal(["Next match", "Previous match", "Match case on or off", "Match whole word on or off", "Close"],
            help.Sheet.Here!.Rows().Select(row => row.Label));
        Assert.Contains(bar!, workbench.SubViewsDeep());
    }

    [Fact]
    public async Task F1_in_the_replace_field_lists_its_keys_and_Enter_hands_the_field_back()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("cat cat\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        FindBarView? bar = null;
        KeysDialog? help = null;
        var status = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { workbench.Editor.Group.ActiveTab!.FocusContent(); host.App.InjectKey(Key.H.WithCtrl); },
            () => (bar = workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault()) is { HasFocus: true },
            () => { foreach (var c in "cat") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Tab),
            () => bar!.ReplacementHasFocus,
            () => { status = workbench.StatusBar.DisplayedText; host.App.InjectKey(Key.F1); },
            () => (help = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => !workbench.SubViews.OfType<KeysDialog>().Any(),
            () => { foreach (var c in "dog") host.App.InjectKey(new Key(c)); },
            () => bar!.Replacement == "dog");

        Assert.Equal(_fs.Path.GetFullPath("/work/a.txt"), status);
        Assert.Equal(["Replace and go to next", "Previous match", "Replace all", "Find field", "Match case on or off", "Match whole word on or off", "Close"],
            help!.Sheet.Here!.Rows().Select(row => row.Label));
        Assert.Equal("cat", bar!.Query);
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

    // #33/#259: a sidebar item's shortcut shows its tab, revealing the sidebar if needed, and pressed
    // again leaves it up — only ts hides.
    [Fact]
    public async Task Sidebar_item_shortcuts_show_their_tab_and_a_second_press_leaves_it_showing()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("foo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var sidebar = workbench.Sidebar;
        var states = new List<(bool Visible, SidebarTab Tab, bool Focused)>();
        var explorerWasUnfocused = false;
        var focusReadout = "";

        await HostSteps.Run(host,
            // Put the keyboard in the editor first: that's the press the reviewer reported (#236).
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () =>
            {
                explorerWasUnfocused = !sidebar.Explorer.HasFocus;
                host.App.InjectKey(Key.F.WithCtrl.WithShift);
            },
            () =>
            {
                states.Add((workbench.IsSidebarVisible, sidebar.ActiveTab, sidebar.Search.InputsHaveFocus));
                host.App.InjectKey(Key.F.WithCtrl.WithShift);
            },
            () =>
            {
                states.Add((workbench.IsSidebarVisible, sidebar.ActiveTab, sidebar.Search.InputsHaveFocus));
                host.App.InjectKey(Key.E.WithCtrl.WithShift);
            },
            () =>
            {
                states.Add((workbench.IsSidebarVisible, sidebar.ActiveTab, sidebar.Explorer.HasFocus));
                focusReadout = workbench.StatusBar.DisplayedFocus;
                host.App.InjectKey(Key.E.WithCtrl.WithShift);
            },
            () => states.Add((workbench.IsSidebarVisible, sidebar.ActiveTab, sidebar.Explorer.HasFocus)));

        Assert.True(explorerWasUnfocused, "Test precondition: the editor, not the explorer, must hold focus");
        Assert.Equal(
            [(true, SidebarTab.Find, true), (true, SidebarTab.Find, true),
             (true, SidebarTab.Explorer, true), (true, SidebarTab.Explorer, true)],
            states);
        Assert.Equal("Explorer", focusReadout);
    }

    // The palette and the leader run the same command, and #259 is about a command that used to read state.
    [Theory]
    [InlineData(CommandIds.ShowExplorer, SidebarTab.Explorer)]
    [InlineData(CommandIds.FindGlobally, SidebarTab.Find)]
    public async Task A_pane_command_shows_and_focuses_its_tab_however_often_it_runs(string command, SidebarTab tab)
    {
        _fs.AddFile("/work/a.txt", new MockFileData("foo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var states = new List<(bool Visible, SidebarTab Tab, bool Focused)>();

        bool Focused() => tab == SidebarTab.Explorer
            ? workbench.Sidebar.Explorer.HasFocus
            : workbench.Sidebar.Search.InputsHaveFocus;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => commands.TryExecute(command),
            () =>
            {
                states.Add((workbench.IsSidebarVisible, workbench.Sidebar.ActiveTab, Focused()));
                commands.TryExecute(command);
            },
            () =>
            {
                states.Add((workbench.IsSidebarVisible, workbench.Sidebar.ActiveTab, Focused()));
                // From hidden it still reveals the tab, as it always has.
                commands.TryExecute(CommandIds.ToggleSidebar);
            },
            () => !workbench.IsSidebarVisible,
            () => commands.TryExecute(command),
            () => states.Add((workbench.IsSidebarVisible, workbench.Sidebar.ActiveTab, Focused())));

        Assert.Equal([(true, tab, true), (true, tab, true), (true, tab, true)], states);
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
            () => workbench.StatusBar.DisplayedPosition == "Ln 3, Col 6 (3 selected)",
            () => host.App.InjectKey(Key.Esc));
    }

    // Ctrl+Shift+F is also how you stop replacing: the row goes away rather than staying up for good,
    // and the press after that leaves the pane where it is (#259).
    [Fact]
    public async Task CtrlShiftF_takes_the_replace_row_off_the_find_pane_and_leaves_the_sidebar_up()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("foo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var search = workbench.Sidebar.Search;

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.H.WithCtrl.WithShift),
            () => search.ReplaceVisible,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => !search.ReplaceVisible && workbench.IsSidebarVisible && search.InputsHaveFocus,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift));

        Assert.True(workbench.IsSidebarVisible, "Ctrl+Shift+F should never hide the sidebar");
        Assert.Equal(SidebarTab.Find, workbench.Sidebar.ActiveTab);
        Assert.False(search.ReplaceVisible);
        Assert.True(search.InputsHaveFocus);
    }

    [Fact]
    public async Task Esc_in_the_find_box_stops_a_running_search_and_editing_the_query_starts_afresh()
    {
        foreach (var name in new[] { "a", "b", "c" })
            _fs.AddFile($"/work/{name}.txt", new MockFileData("needle"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var search = workbench.Sidebar.Search;
        var buffers = new GatedBuffers(_fs.Path.GetFullPath("/work/c.txt"));
        search.OpenBuffers = buffers;
        WorkspaceSearchResult? shown = null;
        var whileFinding = "";
        var afterEsc = (Status: "", Query: "", Searching: true, Inputs: false, SameResults: false);

        try
        {
            await HostSteps.Run(host,
                () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
                () => { foreach (var c in "needl") host.App.InjectKey(new Key(c)); },
                () => search.Result.MatchCount == 3,
                () => { shown = search.Result; buffers.Armed = true; host.App.InjectKey(new Key('e')); },
                () => search.StatusText.StartsWith("Finding… 2 files", StringComparison.Ordinal),
                () => { whileFinding = search.StatusText; host.App.InjectKey(Key.Esc); },
                () =>
                {
                    afterEsc = (search.StatusText, search.Query, search.IsSearching, search.InputsHaveFocus, ReferenceEquals(shown, search.Result));
                    buffers.Release();
                    host.App.InjectKey(Key.Backspace);
                },
                () => search.StatusText == "3 in 3 files (not a git repo)");
        }
        finally
        {
            buffers.Release();
        }

        Assert.Equal("Finding… 2 files · Esc stop", whileFinding);
        Assert.Equal(("Stopped · 2 files searched", "needle", false, true, true), afterEsc);
    }

    [Fact]
    public async Task Esc_in_the_find_box_with_no_search_running_still_goes_back_to_the_editor()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("needle\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var search = workbench.Sidebar.Search;
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt")); },
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => { foreach (var c in "needle") host.App.InjectKey(new Key(c)); },
            () => search.Result.MatchCount == 1 && !search.IsSearching,
            () => host.App.InjectKey(Key.Esc));

        Assert.True(tab!.ContentHasFocus, "Esc should still hand the keys to the editor");
        Assert.Equal("1 in 1 file (not a git repo)", search.StatusText);
    }

    // #629: laying out thousands of results from minified files used to hold the UI thread for seconds.
    [Fact]
    public async Task Thousands_of_long_line_matches_never_hold_up_the_keys()
    {
        var line = string.Concat(Enumerable.Repeat("x.exec(a,b);return ", 200));
        var content = string.Join('\n', Enumerable.Repeat(line, 100));
        for (var i = 0; i < 100; i++)
            _fs.AddFile($"/work/d{i % 10}/f{i}.min.js", new MockFileData(content));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var search = workbench.Sidebar.Search;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        var longest = TimeSpan.Zero;
        var timing = false;

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () =>
            {
                host.App.Iteration += (_, _) =>
                {
                    if (!timing) return;
                    var now = clock.Elapsed;
                    if (now - last > longest) longest = now - last;
                    last = now;
                };
                timing = true;
                last = clock.Elapsed;
                host.App.InjectKey(new Key('e'));
            },
            () => search.Result.MatchCount > 0 && !search.IsSearching,
            () => host.App.InjectKey(new Key('x')),
            () => search.Query == "ex" && search.Result.MatchCount > 0 && !search.IsSearching,
            () => host.App.InjectKey(Key.Enter),
            () => search.Results.HasFocus,
            () => host.App.InjectKey(Key.PageDown),
            () => { });

        Assert.Equal(WorkspaceSearch.DefaultMaxMatches, search.Result.MatchCount);
        Assert.True(longest < TimeSpan.FromMilliseconds(500), $"the UI thread was held for {longest.TotalMilliseconds:F0} ms");
    }

    private sealed class GatedBuffers(string gatedPath) : IOpenBuffers
    {
        private readonly ManualResetEventSlim _gate = new();
        private readonly string _gatedPath = gatedPath;

        public volatile bool Armed;

        public void Release() => _gate.Set();

        public IReadOnlyDictionary<string, string> Snapshot() => new Gated(this);

        public int? ReplaceAll(string fullPath, string query, string replacement, MatchOptions options) => null;

        private sealed class Gated(GatedBuffers owner) : IReadOnlyDictionary<string, string>
        {
            public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out string value)
            {
                if (owner.Armed && key == owner._gatedPath) owner._gate.Wait();
                value = null;
                return false;
            }

            public string this[string key] => throw new KeyNotFoundException();
            public IEnumerable<string> Keys => [];
            public IEnumerable<string> Values => [];
            public int Count => 0;
            public bool ContainsKey(string key) => false;
            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench) => BuildHost(workbench, out _);

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out ICommandService commands)
    {
        var service = new CommandService();
        commands = service;
        return new WorkbenchHost(workbench, service, new KeybindingService(service), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}

internal static class ViewTreeExtensions
{
    public static IEnumerable<View> SubViewsDeep(this View view) =>
        view.SubViews.SelectMany(v => v.SubViewsDeep().Prepend(v));
}
