using System.Text.Json.Nodes;

namespace TuiCode.Workbench.Languages;

/// <summary>The server a language runs, keyed by the grammar's language id. <see cref="Install"/> is null for a custom command.</summary>
public sealed record LanguageServerSpec(string LanguageId, string Name, string Command, IReadOnlyList<string> Arguments, string? Install)
{
    public static readonly LanguageServerSpec CSharp = new(
        "csharp", "C#", KnownLanguageServers.CSharpLs.Command, KnownLanguageServers.CSharpLs.Arguments, KnownLanguageServers.CSharpLs.Install);

    public string NotInstalled => Install is null
        ? $"No {Name} language server: {Command} isn't on PATH"
        : $"No {Name} language server. Install: {Install}";
}

public enum LanguageServerState
{
    Loading,
    Ready,
    Missing,
    Stopped,
}

/// <summary>One server for one language and folder, never restarted once it stops. <see cref="StateChanged"/> is raised through <c>post</c>.</summary>
public sealed class LanguageServer : IDisposable
{
    private readonly ILanguageServerLauncher _launcher;
    private readonly Action<Action> _post;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Document> _documents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _progress = new(StringComparer.Ordinal);
    private ILanguageServerProcess? _process;
    private JsonRpcConnection? _connection;
    private bool _initialized;
    private bool _loaded;
    private bool _stopping;

    public LanguageServer(LanguageServerSpec spec, string root, ILanguageServerLauncher launcher, Action<Action> post)
    {
        Spec = spec;
        Root = root;
        _launcher = launcher;
        _post = post;
    }

    public LanguageServerSpec Spec { get; }
    public string Root { get; }
    public LanguageServerState State { get; private set; } = LanguageServerState.Loading;

    /// <summary>Why it stopped, when it didn't stop because it was asked to.</summary>
    public string? Failure { get; private set; }

    /// <summary>How long after start-up a server that reports no loading progress counts as ready.</summary>
    public TimeSpan QuietLoad { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long stopping waits for the server to answer shutdown, and then to exit, before ending it.</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public event EventHandler? StateChanged;

    public void Start()
    {
        var process = _launcher.Launch(Spec.Command, Spec.Arguments, Root);
        if (process is null)
        {
            SetState(LanguageServerState.Missing);
            return;
        }
        var connection = new JsonRpcConnection(process.Output, process.Input)
        {
            RequestHandler = AnswerServer,
        };
        connection.NotificationReceived += OnNotification;
        lock (_gate)
        {
            _process = process;
            _connection = connection;
        }
        connection.Start();
        _ = Task.WhenAny(process.Exited, connection.Completion).ContinueWith(_ => Stopped(null), TaskScheduler.Default);
        _ = InitializeAsync(connection);
    }

    private async Task InitializeAsync(JsonRpcConnection connection)
    {
        try
        {
            await connection.RequestAsync("initialize", InitializeParams()).ConfigureAwait(false);
        }
        catch (Exception e) when (e is JsonRpcException or IOException)
        {
            Stopped(e.Message);
            return;
        }
        connection.Notify("initialized", new JsonObject());
        lock (_gate)
        {
            _initialized = true;
            foreach (var (uri, document) in _documents)
                connection.Notify("textDocument/didOpen", DidOpen(uri, document.Version, document.Text!));
            foreach (var document in _documents.Values) document.Text = null;
        }
        _ = Task.Delay(QuietLoad).ContinueWith(_ =>
        {
            lock (_gate)
                if (_progress.Count == 0) _loaded = true;
            UpdateReadiness();
        }, TaskScheduler.Default);
    }

    private JsonObject InitializeParams()
    {
        var uri = LspLocations.ToUri(Root);
        return new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["clientInfo"] = new JsonObject { ["name"] = "TuiCode" },
            ["rootPath"] = Root,
            ["rootUri"] = uri,
            ["workspaceFolders"] = Folders(),
            ["capabilities"] = new JsonObject
            {
                ["window"] = new JsonObject { ["workDoneProgress"] = true },
                ["workspace"] = new JsonObject { ["workspaceFolders"] = true, ["configuration"] = true },
                ["textDocument"] = new JsonObject
                {
                    ["synchronization"] = new JsonObject { ["didSave"] = true },
                    ["definition"] = new JsonObject { ["linkSupport"] = true },
                },
            },
        };
    }

    private JsonArray Folders() =>
        new(new JsonObject { ["uri"] = LspLocations.ToUri(Root), ["name"] = Path.GetFileName(Root.TrimEnd('/', '\\')) });

    private JsonNode? AnswerServer(string method, JsonNode? parameters) => method switch
    {
        "workspace/configuration" => new JsonArray([.. (parameters?["items"] as JsonArray ?? []).Select(_ => (JsonNode?)null)]),
        "workspace/workspaceFolders" => Folders(),
        "window/workDoneProgress/create" or "client/registerCapability" or "client/unregisterCapability"
            or "window/showMessageRequest" or "window/showDocument" => null,
        _ => throw new JsonRpcException(JsonRpcConnection.MethodNotFound, $"Method not found: {method}"),
    };

    private void OnNotification(string method, JsonNode? parameters)
    {
        if (method != "$/progress" || parameters?["token"] is not { } token) return;
        var kind = parameters["value"]?["kind"]?.GetValue<string>();
        lock (_gate)
        {
            if (kind == "begin") _progress.Add(token.ToJsonString());
            else if (kind == "end" && _progress.Remove(token.ToJsonString()) && _progress.Count == 0) _loaded = true;
            else return;
        }
        UpdateReadiness();
    }

    private void UpdateReadiness()
    {
        bool ready;
        lock (_gate)
        {
            if (State is LanguageServerState.Stopped or LanguageServerState.Missing) return;
            ready = _initialized && _loaded && _progress.Count == 0;
        }
        SetState(ready ? LanguageServerState.Ready : LanguageServerState.Loading);
    }

    public void Open(string path, string text)
    {
        var uri = LspLocations.ToUri(path);
        lock (_gate)
        {
            if (_documents.ContainsKey(uri)) return;
            var document = new Document { Version = 1 };
            _documents[uri] = document;
            if (_initialized) _connection?.Notify("textDocument/didOpen", DidOpen(uri, document.Version, text));
            else document.Text = text;
        }
    }

    public void Change(string path, string text)
    {
        var uri = LspLocations.ToUri(path);
        lock (_gate)
        {
            if (!_documents.TryGetValue(uri, out var document)) return;
            document.Version++;
            if (!_initialized)
            {
                document.Text = text;
                return;
            }
            _connection?.Notify("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = document.Version },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }),
            });
        }
    }

    public void Save(string path, string text)
    {
        var uri = LspLocations.ToUri(path);
        lock (_gate)
        {
            if (!_initialized || !_documents.ContainsKey(uri)) return;
            _connection?.Notify("textDocument/didSave", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri },
                ["text"] = text,
            });
        }
    }

    public void Close(string path)
    {
        var uri = LspLocations.ToUri(path);
        lock (_gate)
        {
            if (!_documents.Remove(uri) || !_initialized) return;
            _connection?.Notify("textDocument/didClose", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri },
            });
        }
    }

    public bool IsOpen(string path)
    {
        lock (_gate) return _documents.ContainsKey(LspLocations.ToUri(path));
    }

    /// <summary>Where the symbol at <paramref name="line"/> and UTF-16 <paramref name="character"/> is defined.</summary>
    public async Task<IReadOnlyList<SourceLocation>> DefinitionAsync(string path, int line, int character)
    {
        JsonRpcConnection? connection;
        lock (_gate) connection = State == LanguageServerState.Ready ? _connection : null;
        if (connection is null) return [];
        var result = await connection.RequestAsync("textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = LspLocations.ToUri(path) },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
        }).ConfigureAwait(false);
        return LspLocations.Parse(result);
    }

    private JsonObject DidOpen(string uri, int version, string text) => new()
    {
        ["textDocument"] = new JsonObject
        {
            ["uri"] = uri,
            ["languageId"] = Spec.LanguageId,
            ["version"] = version,
            ["text"] = text,
        },
    };

    private void Stopped(string? reason)
    {
        ILanguageServerProcess? process;
        lock (_gate)
        {
            if (_stopping || State == LanguageServerState.Stopped) return;
            Failure = reason;
            process = _process;
            _connection = null;
        }
        process?.Kill();
        SetState(LanguageServerState.Stopped);
    }

    private void SetState(LanguageServerState state)
    {
        lock (_gate)
        {
            if (State == state) return;
            State = state;
        }
        _post(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Asks the server to shut down, and ends it if it hasn't within a second.</summary>
    public async Task StopAsync()
    {
        JsonRpcConnection? connection;
        ILanguageServerProcess? process;
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true;
            connection = _connection;
            process = _process;
        }
        if (process is null) return;
        try
        {
            if (connection is not null && _initialized)
            {
                await connection.RequestAsync("shutdown").WaitAsync(StopTimeout).ConfigureAwait(false);
                connection.Notify("exit");
                await process.Exited.WaitAsync(StopTimeout).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is TimeoutException or IOException or JsonRpcException)
        {
        }
        finally
        {
            process.Kill();
            connection?.Dispose();
            process.Dispose();
        }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private sealed class Document
    {
        public int Version { get; set; }

        /// <summary>The text to open it with, held until the server has initialized.</summary>
        public string? Text { get; set; }
    }
}
