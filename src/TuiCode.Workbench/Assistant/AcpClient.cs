using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Assistant;

/// <summary>What the agent can do, from its answer to <c>initialize</c>.</summary>
public sealed record AgentInfo(bool LoadSession, bool EmbeddedContext);

public sealed record PermissionOption(string Id, string Name, string Kind);

/// <summary>A <c>session/request_permission</c>: the tool call the agent wants to make and the answers it accepts.</summary>
/// <param name="Command">The command line, for a tool that runs one.</param>
public sealed record PermissionRequest(string Title, string? Kind, string? Command, IReadOnlyList<PermissionOption> Options)
{
    public bool IsEdit => Kind is "edit" or "delete" or "move";

    public bool IsRead => Kind is "read" or "search" or "think";

    /// <summary>The first option of <paramref name="kind"/> (<c>allow_once</c>, <c>reject_once</c>…), or null.</summary>
    public string? Option(string kind) => Options.FirstOrDefault(o => o.Kind == kind)?.Id;

    public static PermissionRequest Parse(JsonNode? parameters)
    {
        var call = parameters?["toolCall"];
        var input = call?["rawInput"];
        var command = SessionUpdate.StringOf(input?["command"])
            ?? (input?["command"] is JsonArray words ? string.Join(' ', words.Select(SessionUpdate.StringOf)) : null);
        var options = (parameters?["options"] as JsonArray ?? [])
            .Select(o => new PermissionOption(SessionUpdate.StringOf(o?["optionId"]) ?? "", SessionUpdate.StringOf(o?["name"]) ?? "", SessionUpdate.StringOf(o?["kind"]) ?? ""))
            .Where(o => o.Id.Length > 0)
            .ToList();
        return new PermissionRequest(SessionUpdate.StringOf(call?["title"]) ?? "a tool", SessionUpdate.StringOf(call?["kind"]), command, options);
    }
}

/// <summary>What the agent asks of TuiCode. Each is answered off the reading thread, so one may wait on the user.</summary>
public interface IAgentRequests
{
    /// <summary>The file's text, from its open tab where it has one; <paramref name="line"/> is 1-based.</summary>
    Task<string> ReadTextFileAsync(string path, int? line, int? limit);

    /// <summary>The option the user, or the read-only rule, chose; null to cancel.</summary>
    Task<string?> RequestPermissionAsync(PermissionRequest request);

    /// <summary>The agent tried to write <paramref name="path"/>, and was refused.</summary>
    void WriteRefused(string path);
}

public sealed class AgentMissingException(string program) : Exception($"{program} isn't on PATH")
{
    public string Program { get; } = program;
}

/// <summary>
/// One agent over the Agent Client Protocol (#566): JSON-RPC on its stdin and stdout, one object per line.
/// TuiCode is the client: it opens a session in the folder, sends prompts and answers the agent's file and permission requests.
/// </summary>
public sealed class AcpClient : IDisposable
{
    public const int ProtocolVersion = 1;

    private readonly ILanguageServerLauncher _launcher;
    private readonly IAgentRequests _requests;
    private ILanguageServerProcess? _process;
    private JsonRpcConnection? _connection;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    public AcpClient(AgentSpec agent, string root, ILanguageServerLauncher launcher, IAgentRequests requests)
    {
        Agent = agent;
        Root = root;
        _launcher = launcher;
        _requests = requests;
    }

    public AgentSpec Agent { get; }
    public string Root { get; }
    public AgentInfo? Info { get; private set; }
    public string? SessionId { get; private set; }

    /// <summary>Completes when the agent exits or its output closes.</summary>
    public Task Exited => _exited.Task;

    /// <summary>A <c>session/update</c> for this session, raised on the reading thread.</summary>
    public event Action<SessionUpdate>? Updated;

    /// <summary>Starts the agent in <see cref="Root"/> and initializes it.</summary>
    /// <exception cref="AgentMissingException">The program isn't installed.</exception>
    /// <exception cref="JsonRpcException">The agent refused.</exception>
    /// <exception cref="IOException">The agent exited.</exception>
    public async Task StartAsync()
    {
        var process = _launcher.Launch(Agent.Program, Agent.Arguments, Root) ?? throw new AgentMissingException(Agent.Program);
        var connection = new JsonRpcConnection(process.Output, process.Input, JsonRpcFraming.Lines)
        {
            AsyncRequestHandler = AnswerAsync,
        };
        connection.NotificationReceived += OnNotification;
        _process = process;
        _connection = connection;
        connection.Start();
        _ = Task.WhenAny(process.Exited, connection.Completion).ContinueWith(_ => _exited.TrySetResult(), TaskScheduler.Default);

        var answer = await connection.RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["clientCapabilities"] = new JsonObject
            {
                ["fs"] = new JsonObject { ["readTextFile"] = true, ["writeTextFile"] = false },
                ["terminal"] = false,
            },
            ["clientInfo"] = new JsonObject { ["name"] = "TuiCode", ["version"] = AppVersionText() },
        }).ConfigureAwait(false);
        var capabilities = answer?["agentCapabilities"];
        Info = new AgentInfo(
            capabilities?["loadSession"] is JsonValue load && load.TryGetValue<bool>(out var canLoad) && canLoad,
            capabilities?["promptCapabilities"]?["embeddedContext"] is JsonValue embedded && embedded.TryGetValue<bool>(out var canEmbed) && canEmbed);
    }

    private static string AppVersionText() => About.AppVersion.Current;

    public async Task NewSessionAsync()
    {
        var answer = await Connection.RequestAsync("session/new", SessionParams()).ConfigureAwait(false);
        SessionId = SessionUpdate.StringOf(answer?["sessionId"]) ?? throw new JsonRpcException(JsonRpcConnection.InternalError, "The agent didn't open a session.");
    }

    /// <summary>Reopens <paramref name="sessionId"/>, whose history arrives as updates first; false when the agent can't.</summary>
    public async Task<bool> LoadSessionAsync(string sessionId)
    {
        if (Info is not { LoadSession: true }) return false;
        SessionId = sessionId;
        var parameters = SessionParams();
        parameters["sessionId"] = sessionId;
        try
        {
            await Connection.RequestAsync("session/load", parameters).ConfigureAwait(false);
            return true;
        }
        catch (JsonRpcException)
        {
            SessionId = null;
            return false;
        }
    }

    private JsonObject SessionParams() => new() { ["cwd"] = Root, ["mcpServers"] = new JsonArray() };

    /// <summary>Sends a question; completes with the stop reason once the answer is over.</summary>
    public async Task<string?> PromptAsync(IEnumerable<JsonNode> blocks)
    {
        var answer = await Connection.RequestAsync("session/prompt", new JsonObject
        {
            ["sessionId"] = SessionId,
            ["prompt"] = new JsonArray([.. blocks]),
        }).ConfigureAwait(false);
        return SessionUpdate.StringOf(answer?["stopReason"]);
    }

    /// <summary>Asks the agent to stop the answer under way; the prompt then ends as <c>cancelled</c>.</summary>
    public void Cancel()
    {
        if (SessionId is { } id) _connection?.Notify("session/cancel", new JsonObject { ["sessionId"] = id });
    }

    private JsonRpcConnection Connection => _connection ?? throw new InvalidOperationException("The agent hasn't started.");

    private void OnNotification(string method, JsonNode? parameters)
    {
        if (method != "session/update" || SessionUpdate.StringOf(parameters?["sessionId"]) != SessionId) return;
        if (SessionUpdate.Parse(parameters?["update"]) is { } update) Updated?.Invoke(update);
    }

    private async Task<JsonNode?> AnswerAsync(string method, JsonNode? parameters)
    {
        switch (method)
        {
            case "fs/read_text_file":
                var path = SessionUpdate.StringOf(parameters?["path"]) ?? throw new JsonRpcException(-32602, "No path");
                var text = await _requests.ReadTextFileAsync(path, Int(parameters!["line"]), Int(parameters["limit"])).ConfigureAwait(false);
                return new JsonObject { ["content"] = text };
            case "fs/write_text_file":
                _requests.WriteRefused(SessionUpdate.StringOf(parameters?["path"]) ?? "");
                throw new JsonRpcException(-32000, "TuiCode is read-only: it doesn't let agents edit files.");
            case "session/request_permission":
                var chosen = await _requests.RequestPermissionAsync(PermissionRequest.Parse(parameters)).ConfigureAwait(false);
                return new JsonObject
                {
                    ["outcome"] = chosen is null
                        ? new JsonObject { ["outcome"] = "cancelled" }
                        : new JsonObject { ["outcome"] = "selected", ["optionId"] = chosen },
                };
            default:
                throw new JsonRpcException(JsonRpcConnection.MethodNotFound, $"Method not found: {method}");
        }
    }

    private static int? Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _process?.Kill();
        _connection?.Dispose();
        _process?.Dispose();
    }
}
