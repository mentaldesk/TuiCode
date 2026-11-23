using System.Text.Json.Nodes;
using MentalDesk.Tui.Keys;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives the completion list (#705) through the host against a fake language server. Boots a TG Application — serialised (#77).
public class CompletionHostTests : StaticConfigurationTest
{
    private const string Caller = "class Caller\n{\n    void Go()\n    {\n        var sb = new StringBuilder();\n        \n    }\n}\n";

    private const int Row = 5;

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _server = new();
    private readonly InMemorySettingsService _settings = new();
    private CommandService? _commands;

    public CompletionHostTests()
    {
        Add("/work/Caller.cs", Caller);
        _fs.AddFile("/work/README.md", new MockFileData("# Read me"));
        _server.Complete = parameters => AfterDot(parameters) ? Members() : Names();
    }

    // As a server would: members after a dot, else the names in scope.
    private bool AfterDot(JsonNode parameters)
    {
        var path = LspLocations.ToPath(parameters["textDocument"]!["uri"]!.GetValue<string>())!;
        var line = _server.Documents[path].ReplaceLineEndings("\n").Split('\n')[parameters["position"]!["line"]!.GetValue<int>()];
        var start = Completions.WordStart(line, parameters["position"]!["character"]!.GetValue<int>());
        return start > 0 && line[start - 1] == '.';
    }

    private static JsonNode Members() => new JsonArray(
        Item("ToString", 2, "string ToString()", "4"),
        Item("Append", 2, "StringBuilder Append(string?)", "1"),
        Item("AppendLine", 2, "StringBuilder AppendLine()", "2"),
        Item("Capacity", 10, "int Capacity", "3"));

    private static JsonNode Names() => new JsonArray(Item("Console", 7, "class System.Console", "1"), Item("Convert", 7, null, "2"), Item("var", 14, null, "3"));

    private static JsonNode Item(string label, int kind, string? detail, string sort) =>
        new JsonObject { ["label"] = label, ["kind"] = kind, ["detail"] = detail, ["sortText"] = sort };

    [Fact]
    public async Task Typing_a_dot_opens_the_members_under_the_caret_and_the_caret_stays_in_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        CompletionView? list = null;
        var caretY = 0;
        var editorHasFocus = false;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "sb."); },
            () => (list = List(host)) is not null,
            () => { caretY = Tab(workbench).CaretOnScreen!.Value.Y; editorHasFocus = Tab(workbench).ContentHasFocus; });

        Assert.Equal(["Append", "AppendLine", "Capacity", "ToString"], Labels(list!));
        Assert.Equal(0, list!.SelectedItem);
        Assert.Equal(caretY + 1, list.FrameToScreen().Y);
        Assert.Equal(4, list.Frame.Height);
        Assert.Equal(Completions.Widths(list.Matches).Row, list.Frame.Width);
        Assert.False(list.CanFocus);
        Assert.True(editorHasFocus);
        var asked = _server.Received("textDocument/completion").Single()!;
        Assert.Equal(".", asked["context"]!["triggerCharacter"]!.GetValue<string>());
        Assert.Equal(11, asked["position"]!["character"]!.GetValue<int>());
    }

    [Fact]
    public async Task Typing_more_narrows_it_and_Tab_puts_the_selected_name_in_place_of_what_was_typed()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> narrowed = [];

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "sb."); },
            () => List(host) is not null,
            () => { Type(host, "ap"); },
            () => List(host) is { } list && Labels(list).Count == 2,
            () => { narrowed = Labels(List(host)!); host.App.InjectKey(Key.CursorDown); },
            () => host.App.InjectKey(Key.Tab),
            () => List(host) is null);

        Assert.Equal(["Append", "AppendLine"], narrowed);
        Assert.Equal("        sb.AppendLine", Tab(workbench).Lines[Row]);
        Assert.Equal(21, Tab(workbench).CursorCharacter);
        Assert.Single(_server.Received("textDocument/completion"));
    }

    [Fact]
    public async Task Enter_puts_it_in_too_and_Esc_closes_it_leaving_the_text()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var escaped = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "sb.Ca"); },
            () => List(host) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => List(host) is null,
            () => { escaped = Tab(workbench).Lines[Row]; Type(host, "p"); },
            () => Tab(workbench).Lines[Row].EndsWith("Cap", StringComparison.Ordinal),
            () => List(host) is null,
            () => { Type(host, ".T"); },
            () => List(host) is not null,
            () => host.App.InjectKey(Key.Enter),
            () => List(host) is null);

        Assert.Equal("        sb.Ca", escaped);
        Assert.Equal("        sb.Cap.ToString", Tab(workbench).Lines[Row]);
    }

    [Theory]
    [InlineData(' ', "        sb.Ap ")]
    [InlineData('(', "        sb.Ap(")]
    [InlineData(';', "        sb.Ap;")]
    public async Task A_key_that_isnt_part_of_a_name_closes_it_and_types_as_usual(char key, string line)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "sb.Ap"); },
            () => List(host) is not null,
            () => host.App.InjectKey(new Key(key)),
            () => Tab(workbench).Lines[Row] == line,
            () => List(host) is null);

        Assert.Equal(line.Length, Tab(workbench).CursorCharacter);
    }

    [Fact]
    public async Task Left_and_Right_close_it_and_move_the_caret()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var afterLeft = 0;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "sb.Ap"); },
            () => List(host) is not null,
            () => host.App.InjectKey(Key.CursorLeft),
            () => List(host) is null,
            () => { afterLeft = Tab(workbench).CursorCharacter; host.App.InjectKey(Key.CursorRight); },
            () => Tab(workbench).CursorCharacter == 13,
            () => { Type(host, "p"); },
            () => Tab(workbench).Lines[Row] == "        sb.App",
            () => List(host) is not null,
            () => host.App.InjectKey(Key.CursorRight),
            () => List(host) is null);

        Assert.Equal(12, afterLeft);
    }

    [Fact]
    public async Task Typing_the_first_letters_of_a_name_opens_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> labels = [];

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "Con"); },
            () => List(host) is not null,
            () => { labels = Labels(List(host)!); });

        Assert.Equal(["Console", "Convert"], labels);
        Assert.All(_server.Received("textDocument/completion"), p => Assert.Equal(1, p!["context"]!["triggerKind"]!.GetValue<int>()));
    }

    [Fact]
    public async Task An_incomplete_list_asks_again_as_you_type()
    {
        _server.Complete = parameters => new JsonObject { ["isIncomplete"] = true, ["items"] = Names() };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "C"); },
            () => List(host) is not null,
            () => { Type(host, "o"); },
            () => _server.Received("textDocument/completion").Count() == 2,
            () => List(host) is { } list && Labels(list).Count == 2);

        var again = _server.Received("textDocument/completion").Last()!;
        Assert.Equal(3, again["context"]!["triggerKind"]!.GetValue<int>());
        Assert.Equal(10, again["position"]!["character"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_late_answer_for_an_earlier_place_is_dropped()
    {
        using var hold = new ManualResetEventSlim();
        _server.HoldFirstCompletion = hold;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> before = [], after = [];
        var settled = 0;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "s"); },
            () => _server.Documents.Values.Any(d => d.ReplaceLineEndings("\n").Contains("        s\n", StringComparison.Ordinal)),
            () => { Type(host, "b."); },
            () => List(host) is not null,
            () => { before = Labels(List(host)!); hold.Set(); },
            () => _server.Received("textDocument/completion").Count() == 2,
            () => ++settled > 20,
            () => { after = Labels(List(host)!); });

        Assert.Equal(["Append", "AppendLine", "Capacity", "ToString"], before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task With_more_than_one_caret_it_doesnt_open()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); Tab(workbench).AddCursor(TuiCode.Editor.LineDirection.Up); },
            () => { Type(host, "sb."); },
            () => Tab(workbench).Lines[Row].EndsWith("sb.", StringComparison.Ordinal),
            () => { _commands!.TryExecute(CommandIds.ShowCompletions); },
            () => { });

        Assert.Null(List(host));
        Assert.Empty(_server.Received("textDocument/completion"));
        Assert.Equal("Completions need a single caret", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task With_suggestions_off_nothing_opens_by_itself_but_Show_completions_still_works_on_an_empty_spot()
    {
        _settings.Editor = _settings.Editor with { SuggestAsYouType = false };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var openedByItself = false;
        IReadOnlyList<string> shown = [];

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "Con"); },
            () => Tab(workbench).Lines[Row].EndsWith("Con", StringComparison.Ordinal),
            () => { },
            () => { openedByItself = List(host) is not null; Type(host, " "); },
            () => { _commands!.TryExecute(CommandIds.ShowCompletions); },
            () => List(host) is not null,
            () => { shown = Labels(List(host)!); });

        Assert.False(openedByItself);
        Assert.Equal(["Console", "Convert", "var"], shown);
    }

    [Fact]
    public async Task F1_while_it_is_open_lists_its_keys_and_leaves_it_open()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        KeysDialog? help = null;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "sb."); },
            () => List(host) is not null,
            () => host.App.InjectKey(Key.F1),
            () => (help = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => !workbench.SubViews.OfType<KeysDialog>().Any());

        Assert.Equal("Completions", help!.Sheet.Here!.Heading);
        Assert.Equal(["Tab Enter", "↑ ↓ PgUp PgDn", "Esc", "Type", "Any other key"], help.Sheet.Here.Rows().Select(r => r.Keys));
        Assert.NotNull(List(host));
    }

    [Fact]
    public async Task Up_Down_PageUp_and_PageDown_move_the_selection()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var moves = new List<int?>();

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "sb."); },
            () => List(host) is not null,
            () => host.App.InjectKey(Key.PageDown),
            () => { moves.Add(List(host)!.SelectedItem); host.App.InjectKey(Key.CursorUp); },
            () => { moves.Add(List(host)!.SelectedItem); host.App.InjectKey(Key.PageUp); },
            () => { moves.Add(List(host)!.SelectedItem); host.App.InjectKey(Key.CursorDown); },
            () => { moves.Add(List(host)!.SelectedItem); });

        Assert.Equal([3, 2, 0, 1], moves);
        Assert.Equal("        sb.", Tab(workbench).Lines[Row]);
    }

    [Fact]
    public async Task While_the_server_is_loading_nothing_opens_and_Show_completions_says_so()
    {
        _server.HoldLoading = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var said = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => workbench.StatusBar.DisplayedState.Contains("C# ◌ loading", StringComparison.Ordinal),
            () => { Tab(workbench).MoveCursor(Row, 8); },
            () => { Type(host, "sb."); },
            () => Tab(workbench).Lines[Row].EndsWith("sb.", StringComparison.Ordinal),
            () => { },
            () => { _commands!.TryExecute(CommandIds.ShowCompletions); said = workbench.StatusBar.Message; });

        Assert.Null(List(host));
        Assert.Empty(_server.Received("textDocument/completion"));
        Assert.Equal("The C# language server is still loading", said);
    }

    [Fact]
    public async Task With_no_server_nothing_opens_and_Show_completions_says_what_Quick_info_says()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var said = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "README.md"); },
            () => { Type(host, "Con."); },
            () => { },
            () => { _commands!.TryExecute(CommandIds.ShowCompletions); said = workbench.StatusBar.Message; });

        Assert.Null(List(host));
        Assert.Equal("No language server for Markdown", said);
    }

    [Fact]
    public void Show_completions_is_sc_in_the_editor_with_no_key_right_after_Quick_info_in_the_Go_menu_with_hotkey_C()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Show completions", commands.Registered.Single(c => c.Id == CommandIds.ShowCompletions).Label);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.ShowCompletions));
        Assert.Equal("sc", CommandMnemonics.For(CommandIds.ShowCompletions));
        Assert.DoesNotContain(keybindings.Bindings, b => b.CommandId == CommandIds.ShowCompletions);
        var go = host.Menu.Items.Select(i => i.Id).ToList();
        Assert.Equal(go.IndexOf(CommandIds.QuickInfo) + 1, go.IndexOf(CommandIds.ShowCompletions));
        Assert.Equal('c', TuiCode.Workbench.Menus.CommandMenu.Letters[CommandIds.ShowCompletions]);
    }

    private void Add(string path, string content)
    {
        _fs.AddFile(path, new MockFileData(content));
        _server.Files[_fs.Path.GetFullPath(path)] = content;
    }

    private static bool Ready(Workbench.Workbench workbench) =>
        workbench.StatusBar.DisplayedState.Contains("C# ● ready", StringComparison.Ordinal);

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    private static CompletionView? List(WorkbenchHost host) => host.Completion;

    private static IReadOnlyList<string> Labels(CompletionView list) => [.. list.Matches.Select(m => m.Item.Label)];

    private void OpenFile(Workbench.Workbench workbench, string name) =>
        workbench.OpenFile(_fs.FileInfo.New($"/work/{name}"));

    private static TuiCode.Editor.EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        _commands = new CommandService();
        var host = new WorkbenchHost(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            _settings, driverName: DriverRegistry.Names.ANSI, languageServers: _server);
        HostSteps.PinScreenSize(host, 100, 25);
        return host;
    }
}
