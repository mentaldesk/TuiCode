using System.Text.Json.Nodes;
using TuiCode.Abstractions;
using TuiCode.Workbench.Assistant;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

// The ACP client and the tab's controller against a fake agent over in-memory pipes (#566).
public class AssistantControllerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly MockFileSystem _fs = new();
    private readonly FakeAgent _agent = new();
    private readonly AssistantSessions _sessions;
    private readonly AssistantController _controller;
    private readonly Lock _gate = new();

    public AssistantControllerTests()
    {
        _sessions = new AssistantSessions(_fs, _fs.Path.GetFullPath("/home/.tui/TuiCode.assistant.json"));
        _controller = New();
    }

    private string Root => _fs.Path.GetFullPath("/work");

    private AssistantController New()
    {
        var controller = new AssistantController(_agent, _fs, _sessions, action => { lock (_gate) action(); }, new Conversation());
        controller.Configure(AgentPresets.ClaudeCode);
        controller.OpenFolder(Root);
        return controller;
    }

    public void Dispose() => _controller.Dispose();

    private async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            lock (_gate)
                if (condition()) return;
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private static string Chat(AssistantController controller) =>
        string.Join('\n', controller.Conversation.Render(60).Lines.Select(l => l.Text));

    [Fact]
    public async Task Starting_runs_the_agent_in_the_folder_and_opens_a_session_there()
    {
        await _controller.EnsureStartedAsync().WaitAsync(Timeout);

        var (command, arguments, directory) = _agent.Launches.Single();
        Assert.Equal(("npx", "-y @agentclientprotocol/claude-agent-acp", Root), (command, string.Join(' ', arguments), directory));
        var initialize = _agent.Received("initialize").Single()!;
        Assert.True(initialize["clientCapabilities"]!["fs"]!["readTextFile"]!.GetValue<bool>());
        Assert.True(initialize["clientCapabilities"]!["fs"]!["writeTextFile"]!.GetValue<bool>());
        Assert.Equal(Root, _agent.Received("session/new").Single()!["cwd"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_question_goes_out_with_the_lines_as_a_resource_and_the_answer_streams_in_under_the_agent()
    {
        _agent.OnPrompt = async (agent, _) =>
        {
            agent.Say("It records ");
            agent.Tool("t1", "Read EditorTextView.cs");
            agent.Say("the edit.");
            await Task.Yield();
        };
        var context = AskContext.Of(_fs.Path.Combine(Root, "src", "A.cs"), Root, ["a", "b", "c", "d"], (1, 2), 0);

        _controller.Ask("Why?", context);
        await Until(() => !_controller.IsAnswering);

        var prompt = _agent.Received("session/prompt").Single()!["prompt"]!.AsArray();
        Assert.Equal("About lines 2–3 of src/A.cs:\n\nWhy?", prompt[0]!["text"]!.GetValue<string>());
        Assert.Equal("b\nc", prompt[1]!["resource"]!["text"]!.GetValue<string>());
        Assert.EndsWith("/work/src/A.cs#L2-3", prompt[1]!["resource"]!["uri"]!.GetValue<string>());
        Assert.Equal("You · lines 2–3 of src/A.cs\nWhy?\n\nClaude Code\nIt records\n▸ Read EditorTextView.cs\nthe edit.", Chat(_controller));
    }

    [Fact]
    public async Task A_follow_up_goes_to_the_same_session()
    {
        _controller.Ask("One", null);
        await Until(() => !_controller.IsAnswering);
        _controller.Ask("Two", null);
        await Until(() => !_controller.IsAnswering);

        var sessions = _agent.Received("session/prompt").Select(p => p!["sessionId"]!.GetValue<string>()).ToList();
        Assert.Equal(2, sessions.Count);
        Assert.Equal(sessions[0], sessions[1]);
        Assert.Single(_agent.Received("session/new"));
        Assert.Equal("Two", _agent.Received("session/prompt").Last()!["prompt"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stopping_cancels_the_session_and_keeps_what_arrived()
    {
        _agent.OnPrompt = async (agent, _) =>
        {
            agent.Say("Partial");
            await agent.Cancelled.Task;
        };
        _controller.Ask("Long?", null);
        await Until(() => Chat(_controller).Contains("Partial"));

        _controller.Stop();
        await Until(() => !_controller.IsAnswering);

        Assert.Single(_agent.Received("session/cancel"));
        Assert.EndsWith("Claude Code\nPartial\n\nStopped.", Chat(_controller));
    }

    [Fact]
    public async Task Clearing_empties_the_chat_and_the_next_question_starts_a_new_session()
    {
        _controller.Ask("One", null);
        await Until(() => !_controller.IsAnswering);

        _controller.Clear();
        Assert.True(_controller.Conversation.IsEmpty);
        _controller.Ask("Two", null);
        await Until(() => !_controller.IsAnswering);

        var sessions = _agent.Received("session/prompt").Select(p => p!["sessionId"]!.GetValue<string>()).ToList();
        Assert.NotEqual(sessions[0], sessions[1]);
        Assert.Empty(_agent.Received("session/load"));
    }

    [Fact]
    public async Task Reopening_the_folder_reloads_its_last_conversation_through_the_same_path_as_an_answer()
    {
        _controller.Ask("One", null);
        await Until(() => !_controller.IsAnswering);
        var session = _agent.SessionId;
        _agent.History.AddRange([(true, "One"), (false, "Hello")]);

        using var reopened = New();
        await reopened.EnsureStartedAsync().WaitAsync(Timeout);

        Assert.Equal(session, _agent.Received("session/load").Single()!["sessionId"]!.GetValue<string>());
        Assert.Equal("You\nOne\n\nClaude Code\nHello", Chat(reopened));
    }

    [Fact]
    public async Task An_agent_that_cant_reload_starts_empty_and_says_so()
    {
        _controller.Ask("One", null);
        await Until(() => !_controller.IsAnswering);
        _agent.LoadSession = false;

        using var reopened = New();
        await reopened.EnsureStartedAsync().WaitAsync(Timeout);
        await Until(() => Chat(reopened).Length > 0);

        Assert.Empty(_agent.Received("session/load"));
        Assert.StartsWith("The last conversation couldn't be restored", Chat(reopened));
    }

    [Fact]
    public async Task Each_folder_has_its_own_last_conversation()
    {
        _controller.Ask("One", null);
        await Until(() => !_controller.IsAnswering);

        _controller.OpenFolder(_fs.Path.GetFullPath("/other"));
        await _controller.EnsureStartedAsync().WaitAsync(Timeout);

        Assert.Empty(_agent.Received("session/load"));
        Assert.Equal(2, _agent.Received("session/new").Count());
    }

    [Fact]
    public async Task Reading_a_file_with_an_open_tab_gets_the_unsaved_text_and_otherwise_the_disk()
    {
        _fs.AddFile(_fs.Path.Combine(Root, "Open.cs"), new MockFileData("on disk"));
        _fs.AddFile(_fs.Path.Combine(Root, "Closed.cs"), new MockFileData("one\r\ntwo\r\nthree\r\nfour"));
        _controller.OpenBuffer = path => path.EndsWith("Open.cs", StringComparison.Ordinal) ? ["edited", "lines"] : null;
        string? open = null, closed = null;
        _agent.OnPrompt = async (agent, _) =>
        {
            open = (await agent.Ask("fs/read_text_file", new JsonObject { ["path"] = _fs.Path.Combine(Root, "Open.cs") }))!["content"]!.GetValue<string>();
            closed = (await agent.Ask("fs/read_text_file", new JsonObject { ["path"] = _fs.Path.Combine(Root, "Closed.cs"), ["line"] = 2, ["limit"] = 2 }))!["content"]!.GetValue<string>();
        };

        _controller.Ask("Read", null);
        await Until(() => !_controller.IsAnswering);

        Assert.Equal("edited\nlines", open);
        Assert.Equal("two\nthree", closed);
    }

    [Fact]
    public async Task A_new_session_switches_to_the_agents_auto_mode()
    {
        _agent.Modes = ["default", "acceptEdits", "auto"];

        await _controller.EnsureStartedAsync().WaitAsync(Timeout);

        var setMode = _agent.Received("session/set_mode").Single()!;
        Assert.Equal((_agent.SessionId, "auto"), (setMode["sessionId"]!.GetValue<string>(), setMode["modeId"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_loaded_session_switches_to_the_agents_auto_mode()
    {
        _sessions.Save(Root, AgentPresets.ClaudeCode.CommandLine, "earlier");
        _agent.Modes = ["default", "auto"];

        await _controller.EnsureStartedAsync().WaitAsync(Timeout);

        Assert.Single(_agent.Received("session/load"));
        Assert.Equal("earlier", _agent.Received("session/set_mode").Single()!["sessionId"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("default,plan")]
    [InlineData("auto,default")]
    public async Task No_mode_switch_is_sent_without_an_auto_mode_to_switch_to(string? modes)
    {
        _agent.Modes = modes?.Split(',');

        await _controller.EnsureStartedAsync().WaitAsync(Timeout);

        Assert.Empty(_agent.Received("session/set_mode"));
    }

    [Theory]
    [InlineData("edit", null)]
    [InlineData("delete", null)]
    [InlineData("move", null)]
    [InlineData("execute", "git log --oneline -5")]
    [InlineData("fetch", null)]
    public async Task Every_permission_request_is_allowed_always_or_else_once(string kind, string? command)
    {
        string? always = "unset", once = "unset";
        _agent.OnPrompt = async (agent, _) =>
        {
            always = await agent.AskPermission(kind, "A tool", command);
            once = await agent.AskPermission(kind, "A tool", command, offerAlways: false);
        };

        _controller.Ask("Go", null);
        await Until(() => !_controller.IsAnswering);

        Assert.Equal(("always", "allow"), (always, once));
    }

    [Fact]
    public async Task A_write_to_a_file_that_isnt_open_creates_it_and_its_folder()
    {
        var path = _fs.Path.Combine(Root, "new", "B.cs");
        _agent.OnPrompt = (agent, _) => agent.Ask("fs/write_text_file", new JsonObject { ["path"] = path, ["content"] = "class B { }" });

        _controller.Ask("Write", null);
        await Until(() => !_controller.IsAnswering);

        Assert.Equal("class B { }", _fs.File.ReadAllText(path));
    }

    [Fact]
    public async Task A_write_to_an_open_tab_goes_to_its_buffer_and_not_the_disk()
    {
        var path = _fs.Path.Combine(Root, "A.cs");
        _fs.AddFile(path, new MockFileData("before"));
        (string Path, string Text)? buffered = null;
        _controller.WriteBuffer = (p, text) => { buffered = (p, text); return true; };
        _agent.OnPrompt = (agent, _) => agent.Ask("fs/write_text_file", new JsonObject { ["path"] = path, ["content"] = "after" });

        _controller.Ask("Write", null);
        await Until(() => !_controller.IsAnswering);

        Assert.Equal((path, "after"), buffered);
        Assert.Equal("before", _fs.File.ReadAllText(path));
    }

    [Fact]
    public async Task An_answer_that_changed_files_ends_by_counting_them_once_each_and_pointing_at_review()
    {
        _controller.ReviewKey = () => "Ctrl+Shift+R";
        var a = _fs.Path.Combine(Root, "A.cs");
        var b = _fs.Path.Combine(Root, "B.cs");
        _agent.OnPrompt = async (agent, _) =>
        {
            agent.Tool("r", "Read C.cs", "completed", "read", _fs.Path.Combine(Root, "C.cs"));
            agent.Tool("e1", "Edit A.cs", "pending", "edit", a);
            agent.ToolUpdate("e1", "completed");
            agent.Tool("e2", "Edit A.cs", "completed", "edit", a);
            agent.Tool("e3", "Edit D.cs", "pending", "edit", _fs.Path.Combine(Root, "D.cs"));
            agent.ToolUpdate("e3", "failed");
            await agent.Ask("fs/write_text_file", new JsonObject { ["path"] = b, ["content"] = "b" });
            await agent.Ask("fs/write_text_file", new JsonObject { ["path"] = b, ["content"] = "bb" });
            agent.Say("Done.");
        };

        _controller.Ask("Fix", null);
        await Until(() => !_controller.IsAnswering);

        Assert.EndsWith("Done.\n\nChanged 2 files · Ctrl+Shift+R to review", Chat(_controller));
        Assert.Contains("▸ Edit A.cs", Chat(_controller));
    }

    [Fact]
    public async Task The_count_covers_only_the_files_that_answer_touched()
    {
        _controller.ReviewKey = () => "Ctrl+Shift+R";
        _agent.OnPrompt = (agent, _) =>
        {
            agent.Tool("e1", "Edit A.cs", "completed", "edit", _fs.Path.Combine(Root, "A.cs"));
            return Task.CompletedTask;
        };
        _controller.Ask("One", null);
        await Until(() => !_controller.IsAnswering);

        _agent.OnPrompt = (agent, _) =>
        {
            agent.Tool("e2", "Edit B.cs", "completed", "edit", _fs.Path.Combine(Root, "B.cs"));
            agent.Say("Two done.");
            return Task.CompletedTask;
        };
        _controller.Ask("Two", null);
        await Until(() => !_controller.IsAnswering);

        Assert.EndsWith("Two done.\n\nChanged 1 file · Ctrl+Shift+R to review", Chat(_controller));
    }

    [Fact]
    public async Task An_answer_that_changed_nothing_has_no_changed_files_line()
    {
        _controller.ReviewKey = () => "Ctrl+Shift+R";
        _agent.OnPrompt = (agent, _) =>
        {
            agent.Tool("r", "Read A.cs", "completed", "read", _fs.Path.Combine(Root, "A.cs"));
            agent.Say("Read it.");
            return Task.CompletedTask;
        };

        _controller.Ask("Look", null);
        await Until(() => !_controller.IsAnswering);

        Assert.DoesNotContain("Changed", Chat(_controller));
    }

    [Fact]
    public async Task An_agent_that_isnt_signed_in_shows_its_own_message_and_the_command_to_sign_in()
    {
        _agent.NewSessionError = (-32000, "Authentication required");

        _controller.Ask("Hi", null);
        await Until(() => !_controller.IsAnswering);

        Assert.Contains("Claude Code: Authentication required", Chat(_controller));
        Assert.Contains("Sign in by running `claude auth login` in a terminal", Chat(_controller));
    }

    [Fact]
    public async Task An_agent_that_exits_mid_conversation_says_so_and_the_next_question_starts_it_again()
    {
        _agent.OnPrompt = (agent, _) =>
        {
            agent.Current.Exit();
            return Task.CompletedTask;
        };
        _controller.Ask("One", null);
        await Until(() => Chat(_controller).Contains("exited"));
        await Until(() => !_controller.IsAnswering);

        _agent.OnPrompt = (agent, _) => { agent.Say("Back"); return Task.CompletedTask; };
        _controller.Ask("Two", null);
        await Until(() => Chat(_controller).Contains("Back"));

        Assert.Equal(2, _agent.Launches.Count);
        Assert.Contains("Claude Code exited. Your next question starts it again.", Chat(_controller));
    }

    [Fact]
    public async Task A_missing_agent_says_its_program_isnt_on_PATH()
    {
        _agent.Missing = true;

        _controller.Ask("Hi", null);
        await Until(() => !_controller.IsAnswering);

        Assert.Contains("npx isn't on PATH, so Claude Code can't start.", Chat(_controller));
    }
}
