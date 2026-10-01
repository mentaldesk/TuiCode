using System.Globalization;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// A jump lands with nothing selected (#360). Boots a TG Application — serialised (#77).
public class JumpSelectionHostTests : StaticConfigurationTest
{
    private const int FileLines = 400;

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();

    [Fact]
    public async Task Go_to_line_after_a_shift_selection_lands_with_nothing_selected()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenLongFile(workbench),
            () => { host.App.InjectKey(Key.CursorDown.WithShift); host.App.InjectKey(Key.CursorDown.WithShift); },
            () => Tab(workbench).SelectedText.Length > 0,
            () => GoToLine(host, commands, 300),
            () => Tab(workbench).CursorRow == 299);

        AssertNothingSelected(workbench);
    }

    [Fact]
    public async Task Go_to_line_after_a_find_selection_lands_with_nothing_selected()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenLongFile(workbench),
            () => Tab(workbench).Select(new TextMatch(2, 0, 4)),
            () => GoToLine(host, commands, 300),
            () => Tab(workbench).CursorRow == 299);

        AssertNothingSelected(workbench);
    }

    [Fact]
    public async Task Typing_straight_after_a_jump_inserts_and_deletes_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenLongFile(workbench),
            () => { host.App.InjectKey(Key.CursorDown.WithShift); host.App.InjectKey(Key.CursorDown.WithShift); },
            () => GoToLine(host, commands, 300),
            () => Tab(workbench).CursorRow == 299,
            () => host.App.InjectKey(new Key('X')));

        var lines = Tab(workbench).Lines;
        Assert.Equal(FileLines, lines.Count);
        Assert.Equal("line 1", lines[0]);
        Assert.Equal("Xline 300", lines[299]);
    }

    [Fact]
    public async Task Go_to_symbol_lands_with_nothing_selected()
    {
        _fs.AddFile("/work/Widget.cs", new MockFileData("""
            public class Widget
            {
                public int Count { get; set; }
                public void DoWorkAsync() { }
            }
            """));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/Widget.cs")),
            () => host.App.InjectKey(Key.End.WithShift),
            () => Tab(workbench).SelectedText.Length > 0,
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => Picker(workbench) is { Scanned: true },
            () => { foreach (var c in "dwa") host.App.InjectKey(new Key(c)); },
            () => Picker(workbench)!.VisibleItems.SequenceEqual(["DoWorkAsync"]),
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal(3, Tab(workbench).CursorRow);
        AssertNothingSelected(workbench);
    }

    [Fact]
    public async Task Back_and_forward_land_with_nothing_selected()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var selected = new List<string>();
        void Record() => selected.Add(Tab(workbench).SelectedText);
        void SelectRight() { host.App.InjectKey(Key.CursorRight.WithShift); host.App.InjectKey(Key.CursorRight.WithShift); }

        await HostSteps.Run(host,
            () => OpenLongFile(workbench),
            () => GoToLine(host, commands, 101),
            () => Tab(workbench).CursorRow == 100,
            () => GoToLine(host, commands, 301),
            () => Tab(workbench).CursorRow == 300,
            SelectRight,
            () => commands.TryExecute(CommandIds.NavigateBack),
            () => Tab(workbench).CursorRow == 100,
            Record,
            SelectRight,
            () => commands.TryExecute(CommandIds.NavigateForward),
            () => Tab(workbench).CursorRow == 300,
            Record);

        Assert.Equal(["", ""], selected);
    }

    [Fact]
    public async Task A_find_result_selects_its_match_and_nothing_before_it()
    {
        var lines = Enumerable.Range(1, FileLines).Select(i => $"line {i}").ToArray();
        lines[250] = "the needle";
        _fs.AddFile("/work/haystack.txt", new MockFileData(string.Join('\n', lines)));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var search = workbench.Sidebar.Search;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/haystack.txt")),
            () => { host.App.InjectKey(Key.CursorDown.WithShift); host.App.InjectKey(Key.CursorDown.WithShift); },
            () => Tab(workbench).SelectedText.Length > 0,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => { foreach (var c in "needle") host.App.InjectKey(new Key(c)); },
            () => search.Result.MatchCount == 1,
            () => host.App.InjectKey(Key.Enter),
            () => host.App.InjectKey(Key.Enter),
            () => Tab(workbench).CursorRow == 250);

        Assert.Equal("needle", Tab(workbench).SelectedText);
    }

    private static void AssertNothingSelected(Workbench.Workbench workbench)
    {
        Assert.Equal("", Tab(workbench).SelectedText);
        Assert.DoesNotContain("selected", workbench.StatusBar.DisplayedPosition);
    }

    private static EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

    private static SymbolPickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<SymbolPickerView>().SingleOrDefault();

    private static void GoToLine(WorkbenchHost host, CommandService commands, int line)
    {
        commands.TryExecute(CommandIds.GoToLine);
        foreach (var c in line.ToString(CultureInfo.InvariantCulture)) host.App.InjectKey(new Key(c));
        host.App.InjectKey(Key.Enter);
    }

    private void OpenLongFile(Workbench.Workbench workbench)
    {
        _fs.AddFile("/work/long.txt", new MockFileData(
            string.Join('\n', Enumerable.Range(1, FileLines).Select(i => $"line {i}"))));
        workbench.OpenFile(_fs.FileInfo.New("/work/long.txt"));
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
        _fs.AddDirectory("/work");
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
