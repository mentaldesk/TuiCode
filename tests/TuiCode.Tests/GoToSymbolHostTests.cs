using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `gs` through the host with a real grammar bundle. Boots a TG Application — serialised (#77).
public class GoToSymbolHostTests : StaticConfigurationTest
{
    private const string Widget = """
        public class Widget
        {
            public int Count { get; set; }
            public void DoWorkAsync() { }
            public void Other() { }
        }
        """;

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();

    public GoToSymbolHostTests()
    {
        _fs.AddFile("/work/Widget.cs", new MockFileData(Widget));
        _fs.AddFile("/work/notes.txt", new MockFileData("public class NotCode { }"));
        _fs.AddFile("/work/data.json", new MockFileData("""{ "name": "tuicode" }"""));
    }

    [Fact]
    public async Task Gs_lists_the_files_definitions_in_file_order()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> rows = [];
        SymbolPickerView? view = null;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Widget.cs"); },
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => (view = Picker(workbench)) is { Scanned: true },
            () => { rows = view!.VisibleItems; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("Go to symbol in Widget.cs", view!.Title);
        Assert.Equal(["Widget", "Count", "DoWorkAsync", "Other"], rows);
        Assert.Equal(0, view.SelectedItem);
        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task The_mnemonic_opens_the_picker_and_a_filter_then_Enter_jumps_to_the_declaration()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Widget.cs"); },
            () => MoveDown(host, 5),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.G),
            () => host.App.InjectKey(Key.S),
            () => Picker(workbench) is { Scanned: true },
            () => Type(host, "dwa"),
            () => Picker(workbench)!.VisibleItems.SequenceEqual(["DoWorkAsync"]),
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal(3, Tab(workbench).CursorRow);
    }

    [Fact]
    public async Task The_jump_is_recorded_so_gp_goes_back_to_where_you_were()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Widget.cs"); },
            () => MoveDown(host, 5),
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => Picker(workbench) is { Scanned: true },
            () => Type(host, "dwa"),
            () => Picker(workbench)!.VisibleItems.Count == 1,
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null && Tab(workbench).CursorRow == 3,
            () => { commands.TryExecute(CommandIds.NavigateBack); });

        Assert.Equal(5, Tab(workbench).CursorRow);
    }

    [Fact]
    public async Task Esc_closes_the_picker_and_leaves_the_cursor_where_it_was()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Widget.cs"); },
            () => MoveDown(host, 4),
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => Picker(workbench) is { Scanned: true },
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.Esc),
            () => Picker(workbench) is null);

        Assert.Equal(4, Tab(workbench).CursorRow);
    }

    [Theory]
    [InlineData(null, "No file open")]
    [InlineData("notes.txt", SymbolPickerView.NoSymbols)]
    public async Task Gs_with_nothing_to_list_says_why_and_opens_no_picker(string? file, string message)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var shown = "";

        await HostSteps.Run(host,
            () => { if (file is not null) OpenFile(workbench, file); },
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => { shown = workbench.StatusBar.DisplayedText; });

        // The status bar appends the tab's grammar to whatever message is showing.
        Assert.StartsWith(message, shown, StringComparison.Ordinal);
        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task A_file_whose_grammar_finds_nothing_opens_the_picker_and_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var status = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "data.json"); },
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => Picker(workbench) is { Scanned: true },
            () => { status = Picker(workbench)!.Status; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(SymbolPickerView.NoSymbols, status);
        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task A_big_file_opens_the_picker_straight_away_and_fills_it_in()
    {
        const int classes = 3_000;
        _fs.AddFile("/work/Big.cs", new MockFileData(
            string.Join('\n', Enumerable.Range(0, classes).Select(i => $"public class C{i} {{ }}"))));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var partial = 0;
        var total = 0;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Big.cs"); },
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => Picker(workbench) is not null,
            () => { partial = Picker(workbench)!.VisibleItems.Count; },
            () => Picker(workbench) is { Scanned: true },
            () => { total = Picker(workbench)!.VisibleItems.Count; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(classes, total);
        Assert.InRange(partial, 1, classes - 1);
    }

    [Fact]
    public async Task A_second_gs_without_an_edit_opens_on_the_scan_the_first_one_finished()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var scannedOnOpen = false;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Widget.cs"); },
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => Picker(workbench) is { Scanned: true },
            () => host.App.InjectKey(Key.Esc),
            () => Picker(workbench) is null,
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => { scannedOnOpen = Picker(workbench) is { Scanned: true }; },
            () => host.App.InjectKey(Key.Esc));

        Assert.True(scannedOnOpen);
    }

    [Fact]
    public async Task The_picker_indents_members_under_their_type_until_a_filter_flattens_them()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> outline = [];
        IReadOnlyList<string> filtered = [];

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Widget.cs"); },
            () => { commands.TryExecute(CommandIds.GoToSymbol); },
            () => Picker(workbench) is { Scanned: true },
            () => { outline = Picker(workbench)!.Rows; },
            () => Type(host, "dwa"),
            () => Picker(workbench)!.VisibleItems.SequenceEqual(["DoWorkAsync"]),
            () => { filtered = Picker(workbench)!.Rows; },
            () => host.App.InjectKey(Key.Esc));

        Assert.StartsWith("Widget ", outline[0], StringComparison.Ordinal);
        Assert.EndsWith("class  1", outline[0], StringComparison.Ordinal);
        Assert.StartsWith("  Count ", outline[1], StringComparison.Ordinal);
        Assert.EndsWith("property  3", outline[1], StringComparison.Ordinal);
        Assert.StartsWith("  DoWorkAsync ", outline[2], StringComparison.Ordinal);

        Assert.StartsWith("DoWorkAsync ", Assert.Single(filtered), StringComparison.Ordinal);
    }

    [Fact]
    public void Gs_is_in_the_command_palette_and_the_mnemonics_with_no_default_key()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Go to symbol in file", commands.Registered.Single(c => c.Id == CommandIds.GoToSymbol).Label);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.GoToSymbol));
        Assert.Equal("gs", CommandMnemonics.For(CommandIds.GoToSymbol));
        Assert.DoesNotContain(keybindings.Bindings, b => b.CommandId == CommandIds.GoToSymbol);
    }

    private static SymbolPickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<SymbolPickerView>().SingleOrDefault();

    private static void MoveDown(WorkbenchHost host, int rows)
    {
        for (var i = 0; i < rows; i++) host.App.InjectKey(Key.CursorDown);
    }

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    private void OpenFile(Workbench.Workbench workbench, string name) =>
        workbench.OpenFile(_fs.FileInfo.New($"/work/{name}"));

    private static TuiCode.Editor.EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
