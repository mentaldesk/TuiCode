using MentalDesk.Tui.Dialogs;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Assistant;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `ai` and the AI assistance tab (#566) through the host against a fake agent. Boots a TG Application — serialised (#77).
public class AssistantHostTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeAgent _agent = new();
    private readonly CommandService _commands = new();

    public AssistantHostTests()
    {
        _fs.AddFile("/work/A.cs", new MockFileData(string.Join('\n', Enumerable.Range(1, 100).Select(i => $"// line {i}"))));
    }

    [Fact]
    public async Task ai_with_a_selection_opens_the_tab_focused_and_the_question_goes_out_with_those_lines()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var view = workbench.Panel.Assistant;

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => { Tab(workbench).MoveCursor(40, 0); Shift(host, Key.CursorDown, 3); },
            () => _commands.TryExecute(CommandIds.AiAssistance),
            () => view.InputHasFocus,
            () => Type(host, "Why?"),
            () => host.App.InjectKey(Key.Enter),
            () => Chat(view).Contains("Hello"));

        Assert.Equal(PanelTab.Assistant, workbench.Panel.ActiveTab);
        Assert.StartsWith("● Claude Code · ", view.HeaderText);
        var prompt = _agent.Received("session/prompt").Single()!["prompt"]!.AsArray();
        Assert.Equal("// line 41\n// line 42\n// line 43", prompt[1]!["resource"]!["text"]!.GetValue<string>());
        Assert.Equal("You · lines 41–43 of A.cs\nWhy?\n\nClaude Code\nHello", Chat(view));
        Assert.Equal("", view.Question);
    }

    [Fact]
    public async Task ai_with_no_selection_takes_forty_lines_either_side_of_the_cursor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var view = workbench.Panel.Assistant;

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => Tab(workbench).MoveCursor(50, 0),
            () => _commands.TryExecute(CommandIds.AiAssistance),
            () => view.InputHasFocus);

        Assert.Equal("About lines 11–91 of A.cs", view.ContextText);
    }

    [Fact]
    public async Task The_status_bar_says_answering_until_the_answer_ends_and_Esc_stops_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var view = workbench.Panel.Assistant;
        string? whileAnswering = null;
        _agent.OnPrompt = async (agent, _) =>
        {
            agent.Say("Partial");
            await agent.Cancelled.Task;
        };

        await HostSteps.Run(host,
            () => _commands.TryExecute(CommandIds.AiAssistance),
            () => view.InputHasFocus,
            () => Type(host, "Long?"),
            () => host.App.InjectKey(Key.Enter),
            () => Chat(view).Contains("Partial"),
            () => { whileAnswering = workbench.StatusBar.DisplayedText; },
            () => host.App.InjectKey(Key.Esc),
            () => Chat(view).EndsWith("Stopped."));

        Assert.Contains("AI assistance: answering…", whileAnswering);
        Assert.DoesNotContain("answering", workbench.StatusBar.DisplayedText);
        Assert.Contains("Partial", Chat(view));
    }

    [Fact]
    public async Task Ctrl_L_clears_the_tab_and_the_next_question_starts_a_new_conversation()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var view = workbench.Panel.Assistant;

        await HostSteps.Run(host,
            () => _commands.TryExecute(CommandIds.AiAssistance),
            () => view.InputHasFocus,
            () => Type(host, "One"),
            () => host.App.InjectKey(Key.Enter),
            () => Chat(view).Contains("Hello"),
            () => host.App.InjectKey(Key.L.WithCtrl),
            () => Chat(view) == "",
            () => Type(host, "Two"),
            () => host.App.InjectKey(Key.Enter),
            () => Chat(view).Contains("Hello"));

        Assert.Equal("You\nTwo\n\nClaude Code\nHello", Chat(view));
        Assert.Equal(2, _agent.Received("session/new").Count());
    }

    [Theory]
    [InlineData("Esc", "reject")]
    [InlineData("Ctrl+Enter", "allow")]
    public async Task A_command_the_agent_wants_to_run_asks_with_Deny_focused_and_Enter_does_nothing(string key, string expected)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var view = workbench.Panel.Assistant;
        string? answer = "unset";
        (string? Title, string[]? Buttons, string[]? Kinds, string? Focused) dialog = default;
        _agent.OnPrompt = async (agent, _) =>
        {
            answer = await agent.AskPermission("execute", "git log", "git log --oneline -5");
            agent.Say("Carrying on.");
        };

        await HostSteps.Run(host,
            () => _commands.TryExecute(CommandIds.AiAssistance),
            () => view.InputHasFocus,
            () => Type(host, "Go"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench)?.FocusedButton() is not null,
            () => { dialog = (Confirm(workbench)!.Title, Confirm(workbench)!.Buttons(), Confirm(workbench)!.Kinds(), Confirm(workbench)!.FocusedButton()); },
            () => host.App.InjectKey(Key.Enter),
            () => host.App.InjectKey(Key.TryParse(key, out var k) ? k : throw new ArgumentException(key)),
            () => Chat(view).Contains("Carrying on."));

        Assert.Equal("Claude Code wants to run a command", dialog.Title);
        Assert.Equal(["Ctrl+Enter Allow once", "Esc Deny"], dialog.Buttons);
        Assert.Equal(["Primary", "Secondary"], dialog.Kinds);
        Assert.Equal("Esc Deny", dialog.Focused);
        Assert.Equal(expected, answer);
        Assert.Null(Confirm(workbench));
    }

    [Fact]
    public async Task With_no_agent_found_ai_says_so_and_points_at_settings_without_opening_the_tab()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, installed: false);

        await HostSteps.Run(host,
            () => _commands.TryExecute(CommandIds.AiAssistance));

        Assert.Contains("No AI agent found: npx isn't on PATH. Choose one in Settings › AI Assistance (os)", workbench.StatusBar.DisplayedText);
        Assert.False(workbench.IsAssistantShowing);
        Assert.Empty(_agent.Launches);
    }

    [Fact]
    public async Task Ctrl_G_A_opens_the_tab_in_the_panel_under_the_editor_with_the_question_box_focused()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var view = workbench.Panel.Assistant;

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => host.App.InjectKey(Key.G.WithCtrl),
            () => host.App.InjectKey(Key.A),
            () => view.InputHasFocus);

        Assert.True(workbench.Panel.Visible);
        Assert.Equal(PanelTab.Assistant, workbench.Panel.ActiveTab);
        Assert.Equal("AI assistance", workbench.StatusBar.DisplayedFocus);
    }

    [Fact]
    public async Task ai_again_hides_the_tab_and_gives_the_keys_back_to_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => _commands.TryExecute(CommandIds.AiAssistance),
            () => workbench.Panel.Assistant.InputHasFocus,
            () => _commands.TryExecute(CommandIds.AiAssistance),
            () => workbench.StatusBar.DisplayedFocus == "Editor");

        Assert.False(workbench.IsAssistantShowing);
        Assert.False(workbench.Panel.Visible);
    }

    [Fact]
    public async Task Ctrl_G_A_hides_the_tab_from_its_question_box_and_shows_it_again_from_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => _commands.TryExecute(CommandIds.AiAssistance),
            () => workbench.Panel.Assistant.InputHasFocus,
            () => host.App.InjectKey(Key.G.WithCtrl),
            () => host.App.InjectKey(Key.A),
            () => !workbench.IsAssistantShowing && workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.G.WithCtrl),
            () => host.App.InjectKey(Key.A),
            () => workbench.Panel.Assistant.InputHasFocus);

        Assert.True(workbench.IsAssistantShowing);
        Assert.Equal("", workbench.Panel.Assistant.Question);
    }

    [Fact]
    public async Task Ctrl_PageDown_in_the_panel_switches_its_tab_and_leaves_the_editor_tabs_alone()
    {
        _fs.AddFile("/work/B.cs", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => workbench.OpenFile(_fs.FileInfo.New("/work/B.cs")),
            () => workbench.ShowHistory(true),
            () => _commands.TryExecute(CommandIds.AiAssistance),
            () => workbench.Panel.Assistant.InputHasFocus,
            () => host.App.InjectKey(Key.PageDown.WithCtrl),
            () => workbench.StatusBar.DisplayedFocus == "History");

        Assert.Equal(PanelTab.History, workbench.Panel.ActiveTab);
        Assert.Equal("B.cs", Tab(workbench).File.Name);
    }

    private static string Chat(AssistantView view) => string.Join('\n', view.Chat.Lines.Select(l => l.Text));

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    private static void Shift(WorkbenchHost host, Key key, int times)
    {
        for (var i = 0; i < times; i++) host.App.InjectKey(key.WithShift);
    }

    private static ConfirmDialog? Confirm(Workbench.Workbench workbench) => ConfirmDialogs.Of(workbench);

    private void OpenFile(Workbench.Workbench workbench) => workbench.OpenFile(_fs.FileInfo.New("/work/A.cs"));

    private static TuiCode.Editor.EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, bool installed = true)
    {
        var host = new WorkbenchHost(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, fileSystem: _fs, agents: _agent,
            assistantSessions: new AssistantSessions(_fs, _fs.Path.GetFullPath("/home/.tui/TuiCode.assistant.json")))
        {
            CommandInstalled = _ => installed,
        };
        HostSteps.PinScreenSize(host, 100, 30);
        return host;
    }
}
