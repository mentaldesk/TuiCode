using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// What `tuicode <path>` hands the workbench (#263). Boots a TG Application — serialised (#77).
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
    }

    [Fact]
    public async Task A_file_named_on_the_command_line_opens_ready_to_type_in()
    {
        var target = StartupArguments.Resolve(["src/a.cs"], _fs, "/work", NeverAsked);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenStartupTarget(target),
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

        await HostSteps.Run(host,
            () => workbench.OpenStartupTarget(target),
            () => workbench.Sidebar.Explorer.Root?.FullName == Full("/work/src"));

        Assert.Null(workbench.Editor.Group.ActiveTab);
    }

    private static bool NeverAsked(string path, bool directory) =>
        throw new InvalidOperationException($"An existing path shouldn't be asked about: {path}");

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub)),
            new EditorPart(Syntax), new StatusBarPart());

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
    }
}
