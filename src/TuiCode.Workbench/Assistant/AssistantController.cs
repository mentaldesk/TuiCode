using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Assistant;

/// <summary>
/// Runs the AI assistance tab's agent for the open folder (#566): starts it on the first need, reopens the folder's last
/// conversation, sends questions and keeps <see cref="Conversation"/> in step. Read-only: edits are refused, reads
/// allowed, and anything else is put to the user. Raises <see cref="Changed"/> on the UI thread.
/// </summary>
public sealed class AssistantController : IAgentRequests, IDisposable
{
    private readonly ILanguageServerLauncher _launcher;
    private readonly IFileSystem _fs;
    private readonly AssistantSessions? _sessions;
    private readonly Action<Action> _post;
    private AcpClient? _client;
    private Task? _starting;
    private bool _newSession;
    private string? _root;

    /// <param name="post">Runs an action on the UI thread.</param>
    public AssistantController(ILanguageServerLauncher launcher, IFileSystem fs, AssistantSessions? sessions, Action<Action> post, Conversation conversation)
    {
        Conversation = conversation;
        _launcher = launcher;
        _fs = fs;
        _sessions = sessions;
        _post = post;
    }

    public Conversation Conversation { get; }

    public AgentSpec? Agent { get; private set; }

    public bool IsAnswering { get; private set; }

    /// <summary>The agent is starting or reopening the last conversation.</summary>
    public bool IsStarting { get; private set; }

    /// <summary>The open tab's lines for a path, read on the UI thread, so the agent sees unsaved edits.</summary>
    public Func<string, IReadOnlyList<string>?> OpenBuffer { get; set; } = _ => null;

    /// <summary>Asks the user whether the agent may make a tool call that isn't a read or an edit; true to allow it once.</summary>
    public Func<PermissionRequest, Task<bool>> AskPermission { get; set; } = _ => Task.FromResult(false);

    public event EventHandler? Changed;

    /// <summary>Uses <paramref name="agent"/> from the next question on, stopping the one running if it's different.</summary>
    public void Configure(AgentSpec? agent)
    {
        if (agent == Agent) return;
        Agent = agent;
        Conversation.AgentName = agent?.Name ?? "Agent";
        StopAgent();
        Conversation.Clear();
        Raise();
    }

    /// <summary>The agent runs in <paramref name="root"/> from now on, with that folder's last conversation.</summary>
    public void OpenFolder(string root)
    {
        if (root == _root) return;
        _root = root;
        Conversation.Root = root;
        StopAgent();
        Conversation.Clear();
        Raise();
    }

    /// <summary>Starts the agent and reopens the last conversation, unless it's running already.</summary>
    public Task EnsureStartedAsync()
    {
        if (_client is not null && _starting is not null) return _starting;
        if (Agent is null || _root is null) return Task.CompletedTask;
        var client = new AcpClient(Agent, _root, _launcher, this);
        _client = client;
        client.Updated += update => _post(() =>
        {
            if (!ReferenceEquals(client, _client)) return;
            Conversation.Apply(update);
            Raise();
        });
        _ = client.Exited.ContinueWith(_ => _post(() => OnExited(client)), TaskScheduler.Default);
        IsStarting = true;
        Raise();
        return _starting = StartAsync(client);
    }

    private async Task StartAsync(AcpClient client)
    {
        var agent = client.Agent;
        try
        {
            await client.StartAsync().ConfigureAwait(false);
            var last = _newSession ? null : _sessions?.Load(client.Root, agent.CommandLine);
            var restored = last is not null && await client.LoadSessionAsync(last).ConfigureAwait(false);
            if (!restored) await client.NewSessionAsync().ConfigureAwait(false);
            _sessions?.Save(client.Root, agent.CommandLine, client.SessionId);
            _post(() =>
            {
                _newSession = false;
                if (last is not null && !restored) Conversation.AddNote("The last conversation couldn't be restored: " +
                    $"{agent.Name} can't reopen one. This is a new conversation.");
            });
        }
        catch (Exception e) when (e is AgentMissingException or JsonRpcException or IOException)
        {
            _post(() =>
            {
                if (!ReferenceEquals(client, _client)) return;
                Fail(client, e);
            });
            throw;
        }
        finally
        {
            _post(() =>
            {
                if (ReferenceEquals(client, _client)) IsStarting = false;
                Raise();
            });
        }
    }

    private void Fail(AcpClient client, Exception e)
    {
        StopAgent();
        var agent = client.Agent;
        switch (e)
        {
            case AgentMissingException missing:
                Conversation.AddNote($"{missing.Program} isn't on PATH, so {agent.Name} can't start.");
                break;
            case JsonRpcException refused when agent.SignIn is { } signIn && IsSignIn(refused):
                Conversation.AddNote($"{agent.Name}: {refused.Message}");
                Conversation.AddNote($"Sign in by running `{signIn}` in a terminal, then ask again.");
                break;
            case JsonRpcException refused:
                Conversation.AddNote($"{agent.Name}: {refused.Message}");
                break;
            default:
                Conversation.AddNote($"{agent.Name} exited before it was ready. Ask again to restart it.");
                break;
        }
    }

    // ACP's auth_required error, or an agent that says so in its own words.
    private static bool IsSignIn(JsonRpcException e) =>
        e.Code == -32000 || e.Message.Contains("auth", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("log in", StringComparison.OrdinalIgnoreCase);

    /// <summary>Puts <paramref name="question"/> to the agent, about <paramref name="context"/> where there is one.</summary>
    public void Ask(string question, AskContext? context)
    {
        if (IsAnswering || Agent is null) return;
        Conversation.AddQuestion(question, context?.Label);
        IsAnswering = true;
        Raise();
        _ = AskAsync(question, context);
    }

    private async Task AskAsync(string question, AskContext? context)
    {
        try
        {
            await EnsureStartedAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            _post(Answered);
            return;
        }
        var client = _client;
        if (client is null)
        {
            _post(Answered);
            return;
        }
        try
        {
            var reason = await client.PromptAsync(Blocks(question, context, client.Info?.EmbeddedContext == true)).ConfigureAwait(false);
            _post(() =>
            {
                if (reason == "cancelled") Conversation.AddNote("Stopped.");
                else if (reason is "refusal" or "max_tokens") Conversation.AddNote($"{client.Agent.Name} stopped: {reason.Replace('_', ' ')}.");
                Answered();
            });
        }
        catch (JsonRpcException e)
        {
            _post(() =>
            {
                if (client.Agent.SignIn is { } signIn && IsSignIn(e))
                {
                    Conversation.AddNote($"{client.Agent.Name}: {e.Message}");
                    Conversation.AddNote($"Sign in by running `{signIn}` in a terminal, then ask again.");
                }
                else Conversation.AddNote($"{client.Agent.Name}: {e.Message}");
                Answered();
            });
        }
        catch (IOException)
        {
            _post(Answered);
        }
    }

    private void Answered()
    {
        IsAnswering = false;
        Conversation.EndTurn();
        Raise();
    }

    internal static IEnumerable<JsonNode> Blocks(string question, AskContext? context, bool embedded)
    {
        if (context is null)
        {
            yield return Text(question);
            yield break;
        }
        if (!embedded)
        {
            yield return Text($"About {context.Label}:\n\n```\n{context.Text}\n```\n\n{question}");
            yield break;
        }
        yield return Text($"About {context.Label}:\n\n{question}");
        yield return new JsonObject
        {
            ["type"] = "resource",
            ["resource"] = new JsonObject { ["uri"] = context.Uri, ["mimeType"] = "text/plain", ["text"] = context.Text },
        };
    }

    private static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };

    /// <summary>Stops the answer under way; what arrived stays.</summary>
    public void Stop()
    {
        if (IsAnswering) _client?.Cancel();
    }

    /// <summary>Empties the chat and starts a new conversation with the next question.</summary>
    public void Clear()
    {
        Stop();
        StopAgent();
        _newSession = true;
        if (_root is not null && Agent is not null) _sessions?.Save(_root, Agent.CommandLine, null);
        Conversation.Clear();
        Raise();
    }

    private void OnExited(AcpClient client)
    {
        if (!ReferenceEquals(client, _client)) return;
        _client = null;
        _starting = null;
        IsStarting = false;
        if (IsAnswering) IsAnswering = false;
        Conversation.AddNote($"{client.Agent.Name} exited. Your next question starts it again.");
        Raise();
    }

    private void StopAgent()
    {
        var client = _client;
        _client = null;
        _starting = null;
        IsStarting = false;
        IsAnswering = false;
        client?.Dispose();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public async Task<string> ReadTextFileAsync(string path, int? line, int? limit)
    {
        var open = new TaskCompletionSource<IReadOnlyList<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _post(() => open.TrySetResult(OpenBuffer(path)));
        IReadOnlyList<string> lines = await open.Task.ConfigureAwait(false) ?? ReadDisk(path);
        var first = Math.Max((line ?? 1) - 1, 0);
        var taken = lines.Skip(first);
        if (limit is { } count) taken = taken.Take(count);
        return string.Join('\n', taken);
    }

    private string[] ReadDisk(string path)
    {
        try
        {
            return _fs.File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new JsonRpcException(-32002, $"Couldn't read {path}: {e.Message}");
        }
    }

    public async Task<string?> RequestPermissionAsync(PermissionRequest request)
    {
        if (request.IsEdit)
        {
            _post(() => Declined(request.Title));
            return request.Option("reject_once") ?? request.Option("reject_always");
        }
        if (request.IsRead) return request.Option("allow_once") ?? request.Option("allow_always");
        var asked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _post(() => AskPermission(request).ContinueWith(t => asked.TrySetResult(t is { IsCompletedSuccessfully: true, Result: true }), TaskScheduler.Default));
        var allowed = await asked.Task.ConfigureAwait(false);
        return allowed
            ? request.Option("allow_once") ?? request.Option("allow_always")
            : request.Option("reject_once") ?? request.Option("reject_always");
    }

    public void WriteRefused(string path) => _post(() => Declined(Path.GetFileName(path)));

    private void Declined(string what)
    {
        Conversation.AddNote($"TuiCode declined the edit ({what}): it's read-only.");
        Raise();
    }

    public void Dispose() => StopAgent();
}
