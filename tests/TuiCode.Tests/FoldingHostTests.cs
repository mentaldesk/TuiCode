using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Toggle fold, Fold all and Unfold all through the workbench (#477). Boots a TG Application — serialised (#77).
public class FoldingHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    public FoldingHostTests()
    {
        _fs.AddFile("/work/a.cs", new MockFileData(string.Join('\n',
            "class A", "{", "    void One()", "    {", "        Run();", "    }", "", "    void Two()", "    {", "        Run();", "    }", "}")));
        _fs.AddFile("/work/flat.txt", new MockFileData("one\ntwo\n"));
    }

    [Fact]
    public async Task Ctrl_T_F_folds_the_block_and_the_title_turns_to_Unfold_block()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        EditorTab? tab = null;
        string? before = null;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs"));
                tab.MoveCursor(2, 4);
                tab.FocusContent();
            },
            () => { before = Title(commands, CommandIds.ToggleFold); return true; },
            () => host.App.InjectKey(Key.T.WithCtrl),
            () => host.App.InjectKey(Key.F),
            () => tab!.CursorOnFold);

        Assert.Equal("Fold block", before);
        Assert.Equal("Unfold block", Title(commands, CommandIds.ToggleFold));
    }

    [Fact]
    public async Task Fold_all_and_unfold_all_say_how_many_blocks_they_moved()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        string? folded = null;

        await HostSteps.Run(host,
            () =>
            {
                var tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs"));
                tab.MoveCursor(4, 8);
                tab.FocusContent();
            },
            () => commands.TryExecute(CommandIds.FoldAll),
            () => { folded = workbench.StatusBar.Message; return commands.TryExecute(CommandIds.UnfoldAll); });

        Assert.Equal("Folded 1 block", folded);
        Assert.Equal("Unfolded 1 block", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task Toggle_fold_where_there_is_no_block_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () =>
            {
                var tab = workbench.Editor.Open(_fs.FileInfo.New("/work/flat.txt"));
                tab.FocusContent();
            },
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => commands.TryExecute(CommandIds.ToggleFold));

        Assert.Equal("No block to fold here", workbench.StatusBar.Message);
    }

    [Fact]
    public void The_three_are_editor_commands_in_the_View_menu_with_their_mnemonics()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        string[] ids = [CommandIds.ToggleFold, CommandIds.FoldAll, CommandIds.UnfoldAll];

        Assert.All(ids, id => Assert.Equal(CommandScope.Editor, commands.Registered.Single(c => c.Id == id).Scope));
        Assert.Equal(["Toggle block fold", "Fold all", "Unfold all"], ids.Select(id => commands.Registered.Single(c => c.Id == id).Label));
        Assert.Equal(["tbf", "fa", "ua"], ids.Select(CommandMnemonics.For));
        Assert.Subset(CommandMenu.Layout.Single(m => m.Title == "_View").Ids.ToHashSet(), ids.ToHashSet());
    }

    private static string Title(CommandService commands, string id) => commands.Registered.Single(c => c.Id == id).Title;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()),
            new EditorPart(new SyntaxHighlighter(GrammarBundle.Load())), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
