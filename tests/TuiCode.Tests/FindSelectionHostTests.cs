using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// A match Find selects behaves like a Shift selection (#361). Boots a TG Application — serialised (#77).
public class FindSelectionHostTests : StaticConfigurationTest
{
    private const int FileLines = 400;
    private const int NeedleRow = 250;

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();

    [Theory]
    [InlineData("CursorDown")]
    [InlineData("CursorUp")]
    [InlineData("CursorLeft")]
    [InlineData("CursorRight")]
    [InlineData("Home")]
    [InlineData("End")]
    [InlineData("PageUp")]
    [InlineData("PageDown")]
    public async Task A_plain_move_after_find_drops_the_match_and_moves(string keyName)
    {
        Assert.True(Key.TryParse(keyName, out var key));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var before = (Row: -1, Column: -1);

        await HostSteps.Run(host, [
            () => OpenHaystack(workbench),
            () => FindInBar(host, "needle"),
            () => Tab(workbench).SelectedText == "needle",
            () => { before = (Tab(workbench).CursorRow, Tab(workbench).CursorColumn); Tab(workbench).FocusContent(); },
            () => host.App.InjectKey(key)]);

        AssertNothingSelected(workbench);
        Assert.NotEqual(before, (Tab(workbench).CursorRow, Tab(workbench).CursorColumn));
    }

    [Fact]
    public async Task Down_after_a_find_result_leaves_nothing_selected()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, [
            () => OpenHaystack(workbench),
            .. OpenFindResult(host, workbench),
            () => Tab(workbench).SelectedText == "needle",
            () => host.App.InjectKey(Key.CursorDown),
            () => Tab(workbench).CursorRow == NeedleRow + 1]);

        AssertNothingSelected(workbench);
    }

    [Fact]
    public async Task Shift_arrows_shrink_and_grow_the_selection_from_the_match()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var shrunk = "";
        var grown = "";

        await HostSteps.Run(host, [
            () => OpenHaystack(workbench),
            .. OpenFindResult(host, workbench),
            () => Tab(workbench).SelectedText == "needle",
            () => host.App.InjectKey(Key.CursorLeft.WithShift),
            () => { shrunk = Tab(workbench).SelectedText; host.App.InjectKey(Key.CursorDown.WithShift); },
            () => { grown = Tab(workbench).SelectedText; }]);

        Assert.Equal("needl", shrunk);
        Assert.Equal("needle here\nline 252", grown.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Typing_after_a_find_result_replaces_just_the_match()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, [
            () => OpenHaystack(workbench),
            .. OpenFindResult(host, workbench),
            () => Tab(workbench).SelectedText == "needle",
            () => host.App.InjectKey(new Key('X'))]);

        var lines = Tab(workbench).Lines;
        Assert.Equal(FileLines, lines.Count);
        Assert.Equal("the X here", lines[NeedleRow]);
        Assert.Equal("line 252", lines[NeedleRow + 1]);
    }

    [Fact]
    public async Task Replace_after_a_find_result_replaces_that_match()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, [
            () => OpenHaystack(workbench, extraNeedleRow: 100),
            .. OpenFindResult(host, workbench, resultIndex: 1),
            () => Tab(workbench).CursorRow == NeedleRow,
            () => host.App.InjectKey(Key.H.WithCtrl),
            () => { foreach (var c in "pin") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Enter)]);

        var lines = Tab(workbench).Lines;
        Assert.Equal("the needle here", lines[100]);
        Assert.Equal("the pin here", lines[NeedleRow]);
    }

    private static void FindInBar(WorkbenchHost host, string query)
    {
        host.App.InjectKey(Key.F.WithCtrl);
        foreach (var c in query) host.App.InjectKey(new Key(c));
    }

    private static Delegate[] OpenFindResult(WorkbenchHost host, Workbench.Workbench workbench, int resultIndex = 0) =>
    [
        () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
        () => { foreach (var c in "needle") host.App.InjectKey(new Key(c)); },
        () => workbench.Sidebar.Search.Result.MatchCount > 0,
        () => host.App.InjectKey(Key.Enter),
        () => { for (var i = 0; i < resultIndex; i++) host.App.InjectKey(Key.CursorDown); },
        () => host.App.InjectKey(Key.Enter),
    ];

    private static void AssertNothingSelected(Workbench.Workbench workbench)
    {
        Assert.Equal("", Tab(workbench).SelectedText);
        Assert.DoesNotContain("selected", workbench.StatusBar.DisplayedPosition);
    }

    private static EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

    private void OpenHaystack(Workbench.Workbench workbench, int? extraNeedleRow = null)
    {
        var lines = Enumerable.Range(1, FileLines).Select(i => $"line {i}").ToArray();
        lines[NeedleRow] = "the needle here";
        if (extraNeedleRow is { } row) lines[row] = "the needle here";
        _fs.AddFile("/work/haystack.txt", new MockFileData(string.Join('\n', lines)));
        workbench.OpenFile(_fs.FileInfo.New("/work/haystack.txt"));
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
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
