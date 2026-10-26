using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

/// <summary>
/// Launches in-process fake ACP agents. Each opens sessions, keeps the prompts it's sent, and answers a prompt by
/// running <see cref="OnPrompt"/>, which streams updates and may ask TuiCode things; then it stops with
/// <c>end_turn</c>, or <c>cancelled</c> once a <c>session/cancel</c> has come.
/// </summary>
internal sealed class FakeAgent : ILanguageServerLauncher
{
    private readonly ConcurrentQueue<(string Method, JsonNode? Params)> _received = new();
    private int _sessions;

    public bool Missing { get; set; }

    public bool LoadSession { get; set; } = true;

    /// <summary>The session modes it offers on session/new and session/load, starting in the first; none when null.</summary>
    public string[]? Modes { get; set; }

    /// <summary>Answers session/new with this error, as an agent that isn't signed in does.</summary>
    public (int Code, string Message)? NewSessionError { get; set; }

    /// <summary>What a prompt does before it ends; given the agent and the prompt's parameters.</summary>
    public Func<FakeAgent, JsonNode?, Task> OnPrompt { get; set; } = (agent, _) =>
    {
        agent.Say("Hello");
        return Task.CompletedTask;
    };

    /// <summary>What a session/load replays first, as message chunks: (user?, text).</summary>
    public List<(bool User, string Text)> History { get; } = [];

    public List<(string Command, IReadOnlyList<string> Arguments, string Directory)> Launches { get; } = [];

    public List<InProcessServer> Agents { get; } = [];

    public InProcessServer Current => Agents[^1];

    public string? SessionId { get; private set; }

    public TaskCompletionSource Cancelled { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IEnumerable<JsonNode?> Received(string method) => _received.Where(m => m.Method == method).Select(m => m.Params);

    public ILanguageServerProcess? Launch(string command, IReadOnlyList<string> arguments, string directory)
    {
        Launches.Add((command, arguments, directory));
        if (Missing) return null;
        var agent = new InProcessServer(JsonRpcFraming.Lines);
        agent.Connection.AsyncRequestHandler = (method, parameters) =>
        {
            _received.Enqueue((method, parameters?.DeepClone()));
            return AnswerAsync(method, parameters);
        };
        agent.Connection.NotificationReceived += (method, parameters) =>
        {
            _received.Enqueue((method, parameters?.DeepClone()));
            if (method == "session/cancel") Cancelled.TrySetResult();
        };
        agent.Connection.Start();
        Agents.Add(agent);
        return agent;
    }

    private async Task<JsonNode?> AnswerAsync(string method, JsonNode? parameters)
    {
        switch (method)
        {
            case "initialize":
                return new JsonObject
                {
                    ["protocolVersion"] = 1,
                    ["agentCapabilities"] = new JsonObject
                    {
                        ["loadSession"] = LoadSession,
                        ["promptCapabilities"] = new JsonObject { ["embeddedContext"] = true },
                    },
                };
            case "session/new":
                if (NewSessionError is { } error) throw new JsonRpcException(error.Code, error.Message);
                SessionId = $"session-{Interlocked.Increment(ref _sessions)}";
                return WithModes(new JsonObject { ["sessionId"] = SessionId });
            case "session/load":
                SessionId = parameters!["sessionId"]!.GetValue<string>();
                foreach (var (user, text) in History) Update(user ? "user_message_chunk" : "agent_message_chunk", Text(text));
                return WithModes(new JsonObject());
            case "session/set_mode":
                return new JsonObject();
            case "session/prompt":
                Cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await OnPrompt(this, parameters);
                return new JsonObject { ["stopReason"] = Cancelled.Task.IsCompleted ? "cancelled" : "end_turn" };
            default:
                throw new JsonRpcException(JsonRpcConnection.MethodNotFound, method);
        }
    }

    private JsonObject WithModes(JsonObject answer)
    {
        if (Modes is { } modes)
            answer["modes"] = new JsonObject
            {
                ["currentModeId"] = modes[0],
                ["availableModes"] = new JsonArray([.. modes.Select(id => (JsonNode)new JsonObject { ["id"] = id, ["name"] = id })]),
            };
        return answer;
    }

    public void Say(string text) => Update("agent_message_chunk", Text(text));

    public void Tool(string id, string title, string status = "pending", string? kind = null, params string[] paths)
    {
        var update = new JsonObject { ["sessionUpdate"] = "tool_call", ["toolCallId"] = id, ["title"] = title, ["status"] = status };
        if (kind is not null) update["kind"] = kind;
        if (paths.Length > 0) update["locations"] = new JsonArray([.. paths.Select(p => (JsonNode)new JsonObject { ["path"] = p })]);
        Current.Connection.Notify("session/update", new JsonObject { ["sessionId"] = SessionId, ["update"] = update });
    }

    public void ToolUpdate(string id, string status) =>
        Current.Connection.Notify("session/update", new JsonObject
        {
            ["sessionId"] = SessionId,
            ["update"] = new JsonObject { ["sessionUpdate"] = "tool_call_update", ["toolCallId"] = id, ["status"] = status },
        });

    public Task<JsonNode?> Ask(string method, JsonObject parameters)
    {
        parameters["sessionId"] = SessionId;
        return Current.Connection.RequestAsync(method, parameters);
    }

    /// <summary>Asks permission for a tool call of <paramref name="kind"/>; the answer's option id, or null when cancelled.</summary>
    public async Task<string?> AskPermission(string kind, string title, string? command = null, bool offerAlways = true)
    {
        var call = new JsonObject { ["toolCallId"] = "perm", ["title"] = title, ["kind"] = kind };
        if (command is not null) call["rawInput"] = new JsonObject { ["command"] = command };
        var answer = await Ask("session/request_permission", new JsonObject
        {
            ["toolCall"] = call,
            ["options"] = new JsonArray([
                new JsonObject { ["optionId"] = "allow", ["name"] = "Allow", ["kind"] = "allow_once" },
                .. offerAlways ? [new JsonObject { ["optionId"] = "always", ["name"] = "Always allow", ["kind"] = "allow_always" }] : Array.Empty<JsonNode>(),
                new JsonObject { ["optionId"] = "reject", ["name"] = "Reject", ["kind"] = "reject_once" }]),
        });
        var outcome = answer!["outcome"]!;
        return outcome["outcome"]!.GetValue<string>() == "selected" ? outcome["optionId"]!.GetValue<string>() : null;
    }

    private void Update(string kind, JsonObject content) =>
        Current.Connection.Notify("session/update", new JsonObject
        {
            ["sessionId"] = SessionId,
            ["update"] = new JsonObject { ["sessionUpdate"] = kind, ["content"] = content },
        });

    private static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };
}
