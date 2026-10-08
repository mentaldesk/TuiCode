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

        Assert.Equal("Explorer", help.Title);
        Assert.Equal(
        [
            new("Enter", "Open"),
            new("→ ←", "Expand or collapse"),
            new("Delete, Ctrl+D", "Delete file or folder"),
            new("F2", "Move or rename file or folder"),
            new("Ctrl+X", "Cut file or folder"),
            new("Ctrl+V", "Paste file or folder"),
        ], help.Rows);
    }

    [Fact]
    public async Task The_explorer_column_follows_a_rebind_and_drops_an_unbound_command()
    {
        var help = await HelpFrom(() => _commands.TryExecute(CommandIds.FocusSidebar), "Explorer",
            Override("F2", "-" + CommandIds.RenameFile),
            Override("F6", CommandIds.RenameFile),
            Override("Ctrl+X", "-" + CommandIds.CutFile));

        Assert.Contains(new HelpRow("F6", "Move or rename file or folder"), help.Rows);
        Assert.DoesNotContain(help.Rows, row => row.Description == "Cut file or folder");
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

        Assert.Contains(new HelpRow("Esc", "Cancel cut"), help.Rows);
    }

    [Fact]
    public async Task F1_in_the_find_pane_inputs_lists_the_find_keys()
    {
        var help = await HelpFrom(() => _commands.TryExecute(CommandIds.FindGlobally), "Find");

        Assert.Equal("Find", help.Title);
        Assert.Equal(
        [
            new("Enter, ↓", "Focus find results"),
            new("Tab, Shift+Tab", "Switch find field"),
            new("Ctrl+Enter", "Replace all globally"),
        ], help.Rows);
    }

    [Fact]
    public async Task F1_in_the_find_results_lists_how_to_open_one()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("needle"));
        HelpView? view = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { _commands.TryExecute(CommandIds.FindGlobally); workbench.Sidebar.Search.Query = "needle"; },
            () => workbench.Sidebar.Search.Result.Files.Count > 0,
            () => workbench.Sidebar.Search.FocusResults(),
            () => !workbench.Sidebar.Search.InputsHaveFocus,
            () => host.App.InjectKey(Key.F1),
            () => (view = workbench.SubViews.OfType<HelpView>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));
        var help = view!.Place!;

        Assert.Equal("Find", help.Title);

        Assert.Equal([new HelpRow("Enter", "Open result")], help.Rows);
    }

    [Fact]
    public async Task F1_in_the_editor_lists_the_editors_commands_that_can_run()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo"));
        var help = await HelpFrom(() => Built.OpenFile(_fs.FileInfo.New("/work/a.txt")), "Editor");

        Assert.Equal("Editor", help.Title);
        Assert.Contains(new HelpRow("Alt+↑", "Move line up"), help.Rows);
        Assert.Contains(new HelpRow("Ctrl+Alt+↓", "Add cursor below"), help.Rows);
        Assert.Contains(new HelpRow("Ctrl+G l", "Go to line:column"), help.Rows);
        Assert.DoesNotContain(help.Rows, row => row.Description is "Clear selection" or "Remove secondary cursors");
        Assert.DoesNotContain(help.Rows, row => row.Description == "Go to symbol in file");
        Assert.DoesNotContain(help.Rows, row => row.Description == "Save active editor");
    }

    [Fact]
    public async Task The_editor_column_lists_clear_selection_while_there_is_one()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo"));
        var help = await HelpFrom(() => Built.OpenFile(_fs.FileInfo.New("/work/a.txt")), "Editor",
            before: host => host.App.InjectKey(Key.CursorRight.WithShift));

        Assert.Contains(new HelpRow("Esc", "Clear selection"), help.Rows);
    }

    [Fact]
    public async Task F1_on_the_tab_strip_shows_everywhere_alone()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one"));
        HelpView? view = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => _commands.TryExecute(CommandIds.FocusEditorTabStrip),
            () => workbench.StatusBar.DisplayedFocus == "Tabs",
            () => host.App.InjectKey(Key.F1),
            () => (view = workbench.SubViews.OfType<HelpView>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));

        Assert.Null(view!.Place);
    }

    [Fact]
    public async Task The_editor_column_scrolls_to_its_last_row_in_an_80_by_24_terminal()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one"));
        HelpView? view = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var fits = false;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 80, 24),
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.F1),
            () => (view = workbench.SubViews.OfType<HelpView>().SingleOrDefault()) is not null,
            () => fits = view!.Frame.Height <= workbench.Viewport.Height,
            () => host.App.InjectKey(Key.PageDown),
            () => host.App.InjectKey(Key.PageDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => view!.Body.Viewport.Bottom == view.Body.GetContentSize().Height,
            () => host.App.InjectKey(Key.Esc));

        Assert.True(view!.IsScrollable);
        Assert.True(fits);
    }

    private Workbench.Workbench Built => _built!;
    private Workbench.Workbench? _built;

    private async Task<HelpColumn> HelpFrom(Action focus, string region, params KeybindingOverride[] overrides) =>
        await HelpFrom(focus, region, null, overrides);

    private async Task<HelpColumn> HelpFrom(Action focus, string region, Action<WorkbenchHost>? before, params KeybindingOverride[] overrides)
    {
        HelpView? view = null;
        using var workbench = BuildWorkbench();
        _built = workbench;
        using var host = BuildHost(workbench, overrides);

        await HostSteps.Run(host,
            focus,
            () => workbench.StatusBar.DisplayedFocus == region,
            () => before?.Invoke(host),
            () => host.App.InjectKey(Key.F1),
            () => (view = workbench.SubViews.OfType<HelpView>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));

        return view!.Place!;
    }

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
