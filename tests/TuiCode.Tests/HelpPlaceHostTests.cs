using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// F1's right column for the explorer, the Find pane and the editor (#429). Boots a TG Application — serialised (#77).
public class HelpPlaceHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;

    public HelpPlaceHostTests()
    {
        _keybindings = new KeybindingService(_commands);
    }

    [Fact]
    public async Task F1_in_the_explorer_lists_its_keys_and_its_commands()
    {
        var help = await HelpFrom(() => _commands.TryExecute(CommandIds.FocusSidebar), "Explorer");

        Assert.Equal("Explorer", help.Heading);
        Assert.Equal(
        [
            new("Enter", "Open"),
            new("→ ←", "Expand or collapse"),
            new("Delete, Ctrl+D", "Delete file or folder"),
            new("Shift+Delete", "Delete permanently"),
            new("F2", "Move or rename file or folder"),
            new("Ctrl+X", "Cut file or folder"),
            new("Ctrl+V", "Paste file or folder"),
            .. SidebarHelp.TabKeys,
        ], help.Rows());
    }

    [Fact]
    public async Task The_explorer_column_follows_a_rebind_and_drops_an_unbound_command()
    {
        var help = await HelpFrom(() => _commands.TryExecute(CommandIds.FocusSidebar), "Explorer",
            Override("F2", "-" + CommandIds.RenameFile),
            Override("F6", CommandIds.RenameFile),
            Override("Ctrl+X", "-" + CommandIds.CutFile));

        Assert.Contains(new KeyRow("F6", "Move or rename file or folder"), help.Rows());
        Assert.DoesNotContain(help.Rows(), row => row.Label == "Cut file or folder");
    }

    [Fact]
    public async Task Cancel_cut_is_listed_only_while_something_is_cut()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("abc"));
        var help = await HelpFrom(() =>
        {
            _commands.TryExecute(CommandIds.FocusSidebar);
            Built.Sidebar.Explorer.Cut(_fs.FileInfo.New("/work/a.txt"));
        }, "Explorer");

        Assert.Contains(new KeyRow("Esc", "Cancel cut"), help.Rows());
    }

    [Fact]
    public async Task F1_in_the_find_pane_inputs_lists_the_find_keys()
    {
        var help = await HelpFrom(() => _commands.TryExecute(CommandIds.FindGlobally), "Find");

        Assert.Equal("Find", help.Heading);
        Assert.Equal(
        [
            new("Enter, ↓", "Focus find results"),
            new("Tab, Shift+Tab", "Switch find field"),
            new("Ctrl+Enter", "Replace all globally"),
            new("Alt+C", "Match case on or off"),
            new("Alt+W", "Match whole word on or off"),
            .. SidebarHelp.TabKeys,
        ], help.Rows());
    }

    [Fact]
    public async Task F1_in_the_find_results_lists_how_to_open_one()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("needle"));
        KeysDialog? view = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { _commands.TryExecute(CommandIds.FindGlobally); workbench.Sidebar.Search.Query = "needle"; },
            () => workbench.Sidebar.Search.Result.Files.Count > 0,
            () => workbench.Sidebar.Search.FocusResults(),
            () => !workbench.Sidebar.Search.InputsHaveFocus,
            () => host.App.InjectKey(Key.F1),
            () => (view = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));
        var help = view!.Sheet.Here!;

        Assert.Equal("Find", help.Heading);

        Assert.Equal([new KeyRow("Enter", "Open result"), .. SidebarHelp.TabKeys], help.Rows());
    }

    [Fact]
    public async Task F1_in_AI_assistance_ends_with_the_key_that_hides_it()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one"));
        var help = await HelpFrom(() =>
        {
            Built.OpenFile(_fs.FileInfo.New("/work/a.txt"));
            Built.ShowAssistant(true);
            Built.Assistant.FocusInput();
        }, "AI assistance");

        Assert.Equal(new KeyRow("Ctrl+G a", "Hide AI assistance"), help.Rows().Last());
    }

    [Fact]
    public async Task F1_in_the_AI_assistance_tab_lists_showing_tool_calls_in_full()
    {
        var help = await HelpFrom(() =>
        {
            Built.ShowAssistant(true);
            Built.Panel.Assistant.FocusInput();
        }, "AI assistance");

        Assert.Contains(new KeyRow("Ctrl+T t", "Show tool calls in full"), help.Rows());
    }

    [Fact]
    public async Task F1_in_the_editor_lists_the_editors_commands_that_can_run()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo"));
        var help = await HelpFrom(() => Built.OpenFile(_fs.FileInfo.New("/work/a.txt")), "Editor");

        Assert.Equal("Editor", help.Heading);
        Assert.Contains(new KeyRow("Alt+↑", "Move line up"), help.Rows());
        Assert.Contains(new KeyRow("Ctrl+Alt+↓", "Add cursor below"), help.Rows());
        Assert.Contains(new KeyRow("Ctrl+G l", "Go to line:column"), help.Rows());
        Assert.DoesNotContain(help.Rows(), row => row.Label is "Clear selection" or "Remove secondary cursors");
        Assert.DoesNotContain(help.Rows(), row => row.Label == "Go to symbol in file");
        Assert.DoesNotContain(help.Rows(), row => row.Label == "Save active editor");
    }

    [Fact]
    public async Task The_editor_column_names_column_select_and_word_wrap_both_ways()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo"));
        var help = await HelpFrom(() => Built.OpenFile(_fs.FileInfo.New("/work/a.txt")), "Editor");

        Assert.Contains(help.Rows(), row => row.Label == "Column select on or off");
        Assert.Contains(help.Rows(), row => row.Label == "Word wrap on or off");
    }

    [Fact]
    public async Task The_editor_column_lists_clear_selection_while_there_is_one()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo"));
        var help = await HelpFrom(() => Built.OpenFile(_fs.FileInfo.New("/work/a.txt")), "Editor",
            before: host => host.App.InjectKey(Key.CursorRight.WithShift));

        Assert.Contains(new KeyRow("Esc", "Clear selection"), help.Rows());
    }

    [Fact]
    public async Task F1_on_the_tab_strip_lists_the_tab_keys()
    {
        var help = await TabStripHelp();

        Assert.Equal("Tabs", help.Heading);
        Assert.Equal(
        [
            new("← →", "Switch tab"),
            new("Enter", "Go to the file"),
            new("Ctrl+PageDown", "Next tab"),
            new("Ctrl+PageUp", "Previous tab"),
            new("Ctrl+W", "Close tab"),
            new("Ctrl+\\", "Move to other group"),
        ], help.Rows());
    }

    [Fact]
    public async Task The_tabs_column_lists_undo_close_tab_once_a_tab_has_closed()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one"));
        _fs.AddFile("/work/b.txt", new MockFileData("two"));
        var help = await HelpFrom(() =>
        {
            Built.OpenFile(_fs.FileInfo.New("/work/a.txt"));
            Built.OpenFile(_fs.FileInfo.New("/work/b.txt"));
            _commands.TryExecute(CommandIds.CloseActiveEditor);
            _commands.TryExecute(CommandIds.FocusEditorTabStrip);
        }, "Tabs");

        var rows = help.Rows().ToList();
        var close = rows.IndexOf(new("Ctrl+W", "Close tab"));
        Assert.Equal(new KeyRow("Ctrl+Shift+T", "Undo close tab"), rows[close + 1]);
    }

    [Fact]
    public async Task The_tabs_column_follows_a_rebind_of_next_tab()
    {
        var help = await TabStripHelp(Override("Ctrl+PageDown", "-" + CommandIds.NextEditor), Override("F6", CommandIds.NextEditor));

        Assert.Contains(new KeyRow("F6", "Next tab"), help.Rows());
        Assert.DoesNotContain(new KeyRow("Ctrl+PageDown", "Next tab"), help.Rows());
    }

    [Fact]
    public async Task F1_with_no_file_open_shows_everywhere_alone()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one"));
        KeysDialog? view = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => _commands.TryExecute(CommandIds.CloseActiveEditor),
            () => workbench.Editor.Group.Value is null && workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.F1),
            () => (view = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));

        Assert.Null(view!.Sheet.Here);
    }

    [Fact]
    public async Task F1_in_the_explorer_shows_help_with_everywhere_beside_the_explorer()
    {
        var view = await ExplorerDialog();

        Assert.Equal("Help", view.Title);
        Assert.False(view.Stacked);
        Assert.Equal("Explorer", view.Sheet.Here!.Heading);
        Assert.Equal("Everywhere", view.Sheet.Everywhere!.Heading);
        var headings = view.Sheet.Everywhere.Groups.Select(group => group.Heading!).ToList();
        string[] menus = ["File", "Edit", "Selection", "View", "Go", "Diff", "Review", "Help", "Other"];
        Assert.Equal(headings, menus.Where(headings.Contains));
        Assert.Equal("File", headings[0]);
        Assert.Contains(new KeyRow("Ctrl+O", "Open file or folder"), view.Sheet.Everywhere.Groups[0].Rows);
        Assert.Contains(new KeyRow("F1", "Show keys"), view.Sheet.Everywhere.Groups.Single(group => group.Heading == "Help").Rows);
    }

    [Fact]
    public async Task Everywhere_follows_a_rebind_of_the_command_palette()
    {
        var view = await ExplorerDialog(Override("Ctrl+E", "-" + CommandIds.ShowActions), Override("F6", CommandIds.ShowActions));

        Assert.Contains(new KeyRow("F6", "Show all commands"), Everywhere(view));
        Assert.DoesNotContain(Everywhere(view), row => row.Keys == "Ctrl+E");
    }

    [Fact]
    public async Task A_command_given_a_key_appears_under_its_menu_and_goes_when_unbound()
    {
        var bound = await ExplorerDialog(Override("F7", CommandIds.OpenRecentFolder));
        var unbound = await ExplorerDialog();

        Assert.Contains(new KeyRow("F7", "Open recent folder"), bound.Sheet.Everywhere!.Groups.Single(group => group.Heading == "File").Rows);
        Assert.DoesNotContain(Everywhere(unbound), row => row.Label == "Open recent folder");
    }

    [Fact]
    public async Task Everywhere_leaves_out_a_command_that_cannot_run_here()
    {
        var view = await ExplorerDialog();

        Assert.DoesNotContain(Everywhere(view), row => row.Label == "Match case on or off");
    }

    [Fact]
    public async Task With_no_file_open_everywhere_leaves_out_find_and_replace_in_file()
    {
        var view = await ExplorerDialog();

        Assert.DoesNotContain(Everywhere(view), row => row.Label is "Find in file" or "Replace in file" or "Show or hide file history");
        Assert.Contains(new KeyRow("Ctrl+Shift+H", "Replace globally"), Everywhere(view));
    }

    [Fact]
    public async Task With_a_file_open_everywhere_lists_find_and_replace_in_file()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one"));
        var view = await DialogFrom(() =>
        {
            Built.OpenFile(_fs.FileInfo.New("/work/a.txt"));
            _commands.TryExecute(CommandIds.FocusSidebar);
        }, "Explorer", null);

        Assert.Contains(new KeyRow("Ctrl+F", "Find in file"), Everywhere(view));
        Assert.Contains(new KeyRow("Ctrl+H", "Replace in file"), Everywhere(view));
        Assert.Contains(Everywhere(view), row => row.Label == "Show or hide file history");
    }

    [Fact]
    public async Task In_the_find_pane_the_find_options_are_only_in_the_find_column()
    {
        var view = await DialogFrom(() => _commands.TryExecute(CommandIds.FindGlobally), "Find", null);

        Assert.Contains(view.Sheet.Here!.Rows(), row => row.Label == "Match case on or off");
        Assert.DoesNotContain(Everywhere(view), row => row.Label is "Match case on or off" or "Match whole word on or off");
    }

    [Fact]
    public async Task Everywhere_leaves_out_keys_the_place_already_lists()
    {
        var view = await ExplorerDialog();
        var here = view.Sheet.Here!.Rows().Select(row => row.Keys).ToHashSet();

        Assert.DoesNotContain(Everywhere(view), row => here.Contains(row.Keys));
    }

    [Fact]
    public async Task Everywhere_leaves_editor_commands_to_the_editor_column()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo"));
        var view = await DialogFrom(() => Built.OpenFile(_fs.FileInfo.New("/work/a.txt")), "Editor", null);

        Assert.Contains(view.Sheet.Here!.Rows(), row => row.Label == "Move line up");
        Assert.DoesNotContain(Everywhere(view), row => row.Label == "Move line up");
    }

    [Fact]
    public async Task No_key_reaches_the_editor_while_help_is_open_and_Esc_hands_the_keys_back()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var region = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.F1),
            () => workbench.SubViews.OfType<KeysDialog>().Any(),
            () => host.App.InjectKey(new Key('x')),
            () => host.App.InjectKey(Key.Esc),
            () => !workbench.SubViews.OfType<KeysDialog>().Any(),
            () => { region = workbench.StatusBar.DisplayedFocus; },
            () => host.App.InjectKey(new Key('y')),
            () => workbench.Editor.Group.ActiveTab!.Lines[0] != "one");

        Assert.Equal("Editor", region);
        Assert.Equal("yone", workbench.Editor.Group.ActiveTab!.Lines[0]);
    }

    private async Task<KeyColumn> TabStripHelp(params KeybindingOverride[] overrides)
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one"));
        return await HelpFrom(() =>
        {
            Built.OpenFile(_fs.FileInfo.New("/work/a.txt"));
            _commands.TryExecute(CommandIds.FocusEditorTabStrip);
        }, "Tabs", overrides);
    }

    [Fact]
    public async Task The_editor_column_scrolls_to_its_last_row_in_an_80_by_24_terminal()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one"));
        KeysDialog? view = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        bool fits = false, scrolls = false;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 80, 24),
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.F1),
            () => (view = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => { fits = view!.Frame.Height <= workbench.Viewport.Height; scrolls = view.Body.GetContentSize().Height > view.Body.Viewport.Height; },
            () =>
            {
                host.App.InjectKey(Key.PageDown);
                return view!.Body.Viewport.Bottom >= view.Body.GetContentSize().Height;
            },
            () => host.App.InjectKey(Key.Esc));

        Assert.True(scrolls);
        Assert.True(fits);
        Assert.True(view!.Stacked);
        Assert.Equal("Editor", view.Sheet.Here!.Heading);
    }

    private Workbench.Workbench Built => _built!;
    private Workbench.Workbench? _built;

    private async Task<KeyColumn> HelpFrom(Action focus, string region, params KeybindingOverride[] overrides) =>
        await HelpFrom(focus, region, null, overrides);

    private async Task<KeyColumn> HelpFrom(Action focus, string region, Action<WorkbenchHost>? before, params KeybindingOverride[] overrides) =>
        (await DialogFrom(focus, region, before, overrides)).Sheet.Here!;

    private async Task<KeysDialog> DialogFrom(Action focus, string region, Action<WorkbenchHost>? before, params KeybindingOverride[] overrides)
    {
        KeysDialog? view = null;
        using var workbench = BuildWorkbench();
        _built = workbench;
        using var host = BuildHost(workbench, overrides);

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            focus,
            () => workbench.StatusBar.DisplayedFocus == region,
            () => before?.Invoke(host),
            () => host.App.InjectKey(Key.F1),
            () => (view = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));

        return view!;
    }

    private Task<KeysDialog> ExplorerDialog(params KeybindingOverride[] overrides) =>
        DialogFrom(() => _commands.TryExecute(CommandIds.FocusSidebar), "Explorer", null, overrides);

    private static IEnumerable<KeyRow> Everywhere(KeysDialog view) => view.Sheet.Everywhere!.Rows();

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, params KeybindingOverride[] overrides)
    {
        var settings = new InMemorySettingsService();
        settings.SetKeybindingOverrides(overrides);
        return new(workbench, _commands, _keybindings, new InputScopeStack(), settings, driverName: DriverRegistry.Names.ANSI);
    }

    private static KeybindingOverride Override(string keys, string command) => new(TestKeys.Chord(keys), command);
}
