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
        Assert.False(initialize["clientCapabilities"]!["fs"]!["writeTextFile"]!.GetValue<bool>());
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
    public async Task Writing_a_file_is_refused_nothing_is_written_and_the_chat_says_so()
    {
        var path = _fs.Path.Combine(Root, "A.cs");
        _fs.AddFile(path, new MockFileData("before"));
        JsonRpcException? refused = null;
        _agent.OnPrompt = async (agent, _) =>
        {
            refused = await Assert.ThrowsAsync<JsonRpcException>(() => agent.Ask("fs/write_text_file", new JsonObject { ["path"] = path, ["content"] = "after" }));
        };

        _controller.Ask("Edit", null);
        await Until(() => !_controller.IsAnswering);

        Assert.NotNull(refused);
        Assert.Equal("before", _fs.File.ReadAllText(path));
        Assert.Contains("TuiCode declined the edit (A.cs)", Chat(_controller));
    }

    [Fact]
    public async Task An_edit_is_rejected_and_a_read_allowed_without_asking()
    {
        var asked = 0;
        _controller.AskPermission = _ => { asked++; return Task.FromResult(true); };
        string? edit = "unset", read = "unset";
        _agent.OnPrompt = async (agent, _) =>
        {
            edit = await agent.AskPermission("edit", "Edit A.cs");
            read = await agent.AskPermission("read", "Read A.cs");
        };

        _controller.Ask("Go", null);
        await Until(() => !_controller.IsAnswering);

        Assert.Equal(("reject", "allow", 0), (edit, read, asked));
        Assert.Contains("TuiCode declined the edit (Edit A.cs)", Chat(_controller));
    }

    [Fact]
    public async Task A_command_is_put_to_the_user_and_denying_it_tells_the_agent_and_the_answer_carries_on()
    {
        PermissionRequest? request = null;
        _controller.AskPermission = r => { request = r; return Task.FromResult(false); };
        string? answer = "unset";
        _agent.OnPrompt = async (agent, _) =>
        {
            answer = await agent.AskPermission("execute", "git log", "git log --oneline -5");
            agent.Say("Carrying on.");
        };

        _controller.Ask("Go", null);
        await Until(() => !_controller.IsAnswering);

        Assert.Equal("git log --oneline -5", request!.Command);
        Assert.Equal("reject", answer);
        Assert.EndsWith("Carrying on.", Chat(_controller));
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
