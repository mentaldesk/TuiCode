using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// `tuicode <file>` on a file outside the folder you ran from opens it with no folder (#451). Boots a TG Application.
public class NoFolderHostTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();
    private readonly WorkspaceStateStore _store;

    public NoFolderHostTests()
    {
        _fs.AddFile("/work/src/a.cs", new MockFileData("class A;\n"));
        _fs.AddFile("/tmp/notes.txt", new MockFileData("notes\n"));
        _fs.Directory.SetCurrentDirectory(Full("/work"));
        _store = new WorkspaceStateStore(_fs, "/state.json");
    }

    [Fact]
    public async Task A_file_outside_the_current_folder_opens_with_no_folder_in_the_explorer()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        host.OpenWhenRunning(Resolve("/tmp/notes.txt"));

        await HostSteps.Run(host,
            () => workbench.Editor.Group.ActiveTab is not null,
            () => host.App.InjectKey(new Key('X')),
            () => workbench.Editor.Group.ActiveTab!.Content.StartsWith('X'));

        Assert.Equal(Full("/tmp/notes.txt"), workbench.Editor.Group.ActiveTab?.File.FullName);
        Assert.Null(workbench.Sidebar.Explorer.Root);
        Assert.True(workbench.Sidebar.IsShowingNoFolder);
        Assert.False(workbench.Sidebar.Explorer.Visible);
        Assert.Empty(_store.Folders());
    }

    [Fact]
    public async Task Open_Folder_opens_the_picker_and_the_folder_picked_fills_the_explorer()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        host.OpenWhenRunning(Resolve("/tmp/notes.txt"));
        OpenView? dialog = null;

        await HostSteps.Run(host,
            () => workbench.Editor.Group.ActiveTab is not null,
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.Sidebar.OpenFolderButton.HasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => (dialog = workbench.SubViews.OfType<OpenView>().SingleOrDefault()) is not null,
            () => { dialog!.SubViews.OfType<Button>().Single().InvokeCommand(Command.Accept); },
            () => workbench.Sidebar.Explorer.Root is not null);

        Assert.Equal("Explorer", workbench.StatusBar.DisplayedFocus);
        Assert.Equal(Full("/work"), workbench.Sidebar.Explorer.Root?.FullName);
        Assert.False(workbench.Sidebar.IsShowingNoFolder);
        Assert.True(workbench.Sidebar.Explorer.Visible);
        Assert.True(workbench.Sidebar.Explorer.HasFocus);
    }

    private StartupTarget Resolve(params string[] args) =>
        StartupArguments.Resolve(args, _fs, Full("/work"), (path, _) =>
            throw new InvalidOperationException($"An existing path shouldn't be asked about: {path}"));

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub)),
            new EditorPart(Syntax), new StatusBarPart(), _store);

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
    }
}
