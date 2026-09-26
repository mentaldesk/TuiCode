using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// What `tuicode <path>...` hands the workbench (#263, #266), handed over the way `Program.cs` does — before the
// loop starts, opened on its first iteration. Boots a TG Application — serialised (#77).
public class StartupHostTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();

    public StartupHostTests()
    {
        _fs.AddDirectory("/work");
        _fs.AddFile("/work/src/a.cs", new MockFileData("class A;\n"));
        _fs.AddFile("/work/src/long.cs", new MockFileData(string.Concat(
            Enumerable.Range(1, 200).Select(n => $"// line {n} padded out so a column lands somewhere\n"))));
    }

    [Fact]
    public async Task A_file_named_on_the_command_line_opens_ready_to_type_in()
    {
        var target = StartupArguments.Resolve(["src/a.cs"], _fs, "/work", NeverAsked);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        host.OpenWhenRunning(target);

        await HostSteps.Run(host,
            () => workbench.Editor.Group.ActiveTab is not null,
            () => host.App.InjectKey(new Key('X')),
            () => workbench.Editor.Group.ActiveTab!.Content.StartsWith('X'));

        Assert.Equal("Editor", workbench.StatusBar.DisplayedFocus);
        Assert.Equal(Full("/work/src/a.cs"), workbench.Editor.Group.ActiveTab?.File.FullName);
        Assert.Equal(Full("/work"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    [Fact]
    public async Task A_folder_named_on_the_command_line_roots_the_explorer_with_nothing_open()
    {
        var target = StartupArguments.Resolve(["src"], _fs, "/work", NeverAsked);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        host.OpenWhenRunning(target);

        await HostSteps.Run(host,
            () => workbench.Sidebar.Explorer.Root?.FullName == Full("/work/src"));

        Assert.Null(workbench.Editor.Group.ActiveTab);
    }

    [Theory]
    [InlineData("src/long.cs:128", 127, 0, "Ln 128, Col 1")]
    [InlineData("src/long.cs:128:9", 127, 8, "Ln 128, Col 9")]
    public async Task A_position_on_the_command_line_puts_the_cursor_there(
        string argument, int row, int column, string shown)
    {
        var target = StartupArguments.Resolve([argument], _fs, "/work", NeverAsked);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        host.OpenWhenRunning(target);

        await HostSteps.Run(host,
            () => workbench.Editor.Group.ActiveTab is not null);

        var tab = workbench.Editor.Group.ActiveTab!;
        Assert.Equal(row, tab.CursorRow);
        Assert.Equal(column, tab.CursorColumn);
        Assert.Equal(shown, workbench.StatusBar.DisplayedPosition);
        // Centred, not merely on screen: TG's own scroll-to-cursor would leave the line on the bottom row.
        // The viewport can grow after the centring, so pin the middle third rather than the exact row.
        Assert.InRange(row - tab.TopRow, tab.VisibleRows / 3, tab.VisibleRows * 2 / 3);
    }

    [Fact]
    public async Task A_line_past_the_end_of_the_file_lands_on_the_last_one()
    {
        var target = StartupArguments.Resolve(["src/a.cs:9999"], _fs, "/work", NeverAsked);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        host.OpenWhenRunning(target);

        await HostSteps.Run(host,
            () => workbench.Editor.Group.ActiveTab is not null);

        Assert.Equal("Ln 2, Col 1", workbench.StatusBar.DisplayedPosition);
    }

    [Fact]
    public async Task The_file_named_on_the_command_line_wins_over_the_folders_restored_tabs()
    {
        _fs.AddFile("/work/notes.md", new MockFileData("# notes\n"));
        var store = new WorkspaceStateStore(_fs, "/state.json");
        store.Save(Full("/work"), new WorkspaceState([Full("/work/notes.md")], Full("/work/notes.md")));
        var target = StartupArguments.Resolve(["src/long.cs:128"], _fs, "/work", NeverAsked);
        using var workbench = BuildWorkbench(store);
        using var host = BuildHost(workbench);
        host.OpenWhenRunning(target);

        await HostSteps.Run(host,
            () => workbench.Editor.Group.ActiveTab?.File.Name == "long.cs",
            () => host.App.InjectKey(new Key('X')),
            () => workbench.Editor.Group.ActiveTab!.Lines[127].StartsWith('X'));

        Assert.Equal([Full("/work/notes.md"), Full("/work/src/long.cs")],
            workbench.Editor.Group.Tabs.Select(t => t.File.FullName));
        Assert.Equal("Ln 128, Col 2", workbench.StatusBar.DisplayedPosition);
    }

    [Fact]
    public async Task Several_files_named_on_the_command_line_all_open_with_the_first_active()
    {
        _fs.AddFile("/work/notes.md", new MockFileData("# notes\n"));
        var target = StartupArguments.Resolve(["src/a.cs:1", "src/long.cs:128", "notes.md"], _fs, "/work", NeverAsked);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        host.OpenWhenRunning(target);

        await HostSteps.Run(host,
            () => workbench.Editor.Group.Tabs.Count == 3,
            () => host.App.InjectKey(new Key('X')),
            () => workbench.Editor.Group.ActiveTab!.Content.StartsWith('X'));

        Assert.Equal(
            [Full("/work/src/a.cs"), Full("/work/src/long.cs"), Full("/work/notes.md")],
            workbench.Editor.Group.Tabs.Select(tab => tab.File.FullName));
        Assert.Equal(Full("/work/src/a.cs"), workbench.Editor.Group.ActiveTab?.File.FullName);
        Assert.Equal(Full("/work"), workbench.Sidebar.Explorer.Root?.FullName);

        // Each tab kept the position it was given, not just the one you can see.
        var behind = workbench.Editor.Group.Tabs.Single(tab => tab.File.Name == "long.cs");
        Assert.Equal(127, behind.CursorRow);
    }

    private static bool NeverAsked(string path, bool directory) =>
        throw new InvalidOperationException($"An existing path shouldn't be asked about: {path}");

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private Workbench.Workbench BuildWorkbench(WorkspaceStateStore? workspaceState = null) =>
        new(new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub)),
            new EditorPart(Syntax), new StatusBarPart(), workspaceState);

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
    }
}
