using System.Text.Json.Nodes;

namespace TuiCode.Workbench.Languages;

/// <summary>The server a language runs, keyed by the grammar's language id. <see cref="Install"/> is null for a custom command.</summary>
public sealed record LanguageServerSpec(string LanguageId, string Name, string Command, IReadOnlyList<string> Arguments, string? Install)
{
    public static readonly LanguageServerSpec CSharp = new(
        "csharp", "C#", KnownLanguageServers.CSharpLs.Command, KnownLanguageServers.CSharpLs.Arguments, KnownLanguageServers.CSharpLs.Install);

    public string Loading => $"{Command} is loading the {(LanguageId == "csharp" ? "solution" : "folder")}…";

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
    private readonly Dictionary<string, Document> _documents = new(LspLocations.PathComparer);
    private readonly HashSet<string> _progress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(LspLocations.PathComparer);
    private ILanguageServerProcess? _process;
    private JsonRpcConnection? _connection;
    private bool _initialized;
    private bool _loaded;
    private bool _stopping;
    private JsonNode? _renameProvider;

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

    /// <summary>The server's problems for an open file, replacing any it reported before. Raised through <c>post</c>.</summary>
    public event EventHandler<FileProblems>? ProblemsReported;

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
            var answer = await connection.RequestAsync("initialize", InitializeParams()).ConfigureAwait(false);
            lock (_gate) _renameProvider = answer?["capabilities"]?["renameProvider"]?.DeepClone();
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
        UpdateReadiness();
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
                ["workspace"] = new JsonObject
                {
                    ["workspaceFolders"] = true,
                    ["workspaceEdit"] = new JsonObject { ["documentChanges"] = true },
                    ["configuration"] = true,
                    ["symbol"] = new JsonObject
                    {
                        ["symbolKind"] = new JsonObject { ["valueSet"] = new JsonArray([.. Enumerable.Range(1, 26).Select(k => (JsonNode)k)]) },
                    },
                },
                ["textDocument"] = new JsonObject
                {
                    ["publishDiagnostics"] = new JsonObject(),
                    ["synchronization"] = new JsonObject { ["didSave"] = true },
                    ["definition"] = new JsonObject { ["linkSupport"] = true },
                    ["documentSymbol"] = new JsonObject { ["hierarchicalDocumentSymbolSupport"] = true },
                    ["hover"] = new JsonObject { ["contentFormat"] = new JsonArray("markdown", "plaintext") },
                    ["rename"] = new JsonObject { ["prepareSupport"] = true },
                },
                ["experimental"] = new JsonObject { ["csharp"] = new JsonObject { ["metadataUris"] = true } },
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
        if (method == "textDocument/publishDiagnostics")
        {
            Reported(LspDiagnostics.Parse(parameters));
            return;
        }
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

    private void Reported(FileProblems? problems)
    {
        if (problems is null || !IsOpen(problems.Path)) return;
        _post(() => ProblemsReported?.Invoke(this, problems));
    }

    // Decided and set under one lock, so a check that read the flags earlier can't overwrite a later one.
    private void UpdateReadiness()
    {
        lock (_gate)
        {
            if (State is LanguageServerState.Stopped or LanguageServerState.Missing) return;
            var state = _initialized && _loaded && _progress.Count == 0 ? LanguageServerState.Ready : LanguageServerState.Loading;
            if (State == state) return;
            State = state;
        }
        _post(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    public void Open(string path, string text)
    {
        var uri = LspLocations.ToUri(path);
        lock (_gate)
        {
            if (_aliases.ContainsKey(path)) return;
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
            if (_aliases.ContainsKey(path)) return;
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
            if (_aliases.ContainsKey(path)) return;
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

    /// <summary>Asks about <paramref name="path"/> as the server's own <paramref name="uri"/>, which the server already has open.</summary>
    public void Alias(string path, string uri)
    {
        lock (_gate) _aliases[path] = uri;
    }

    private string UriOf(string path)
    {
        lock (_gate) return _aliases.GetValueOrDefault(path) ?? LspLocations.ToUri(path);
    }

    /// <summary>csharp-ls's decompiled source for a <c>csharp:/</c> <paramref name="uri"/>, or null when it can't give it.</summary>
    public async Task<DecompiledSource?> DecompiledAsync(string uri) =>
        DecompiledSource.Parse(await RequestAsync("csharp/metadata", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
        }).ConfigureAwait(false));

    /// <summary>Where the symbol at <paramref name="line"/> and UTF-16 <paramref name="character"/> is defined.</summary>
    public async Task<IReadOnlyList<SourceLocation>> DefinitionAsync(string path, int line, int character) =>
        LspLocations.Parse(await RequestAsync("textDocument/definition", Position(path, line, character)).ConfigureAwait(false));

    /// <summary>What the server says the symbol at <paramref name="line"/> and UTF-16 <paramref name="character"/> is.</summary>
    public async Task<IReadOnlyList<HoverPart>> HoverAsync(string path, int line, int character) =>
        LspHover.Parse(await RequestAsync("textDocument/hover", Position(path, line, character)).ConfigureAwait(false));

    /// <summary>Where the symbol at <paramref name="line"/> and UTF-16 <paramref name="character"/> is used, leaving out its declaration.</summary>
    public async Task<IReadOnlyList<SourceLocation>> ReferencesAsync(string path, int line, int character)
    {
        var parameters = Position(path, line, character);
        parameters["context"] = new JsonObject { ["includeDeclaration"] = false };
        return LspLocations.Parse(await RequestAsync("textDocument/references", parameters).ConfigureAwait(false));
    }

    /// <summary>The symbol declared at <paramref name="location"/>, named after its type (<c>LineDiff.Hunks</c>), or null.</summary>
    public async Task<string?> SymbolNameAsync(SourceLocation location)
    {
        var symbols = await RequestAsync("textDocument/documentSymbol", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = UriOf(location.Path) },
        }).ConfigureAwait(false);
        return LspSymbols.QualifiedName(symbols, location.Line, location.Character);
    }

    /// <summary>Whether the server said it can rename.</summary>
    public bool CanRename
    {
        get { lock (_gate) return _renameProvider is JsonObject || _renameProvider?.GetValueKind() == System.Text.Json.JsonValueKind.True; }
    }

    private bool CanPrepareRename
    {
        get { lock (_gate) return (_renameProvider as JsonObject)?["prepareProvider"]?.GetValueKind() == System.Text.Json.JsonValueKind.True; }
    }

    /// <summary>What can be renamed at <paramref name="line"/> and UTF-16 <paramref name="character"/>: the default when the server doesn't say, null when it says nothing can.</summary>
    public async Task<RenameTarget?> PrepareRenameAsync(string path, int line, int character) =>
        CanPrepareRename
            ? LspRename.ParsePrepare(await RequestAsync("textDocument/prepareRename", Position(path, line, character)).ConfigureAwait(false))
            : new RenameTarget(null, null);

    /// <summary>The edits that rename the symbol at <paramref name="line"/> and UTF-16 <paramref name="character"/> to <paramref name="newName"/>.</summary>
    public async Task<IReadOnlyList<FileEdit>> RenameAsync(string path, int line, int character, string newName)
    {
        var parameters = Position(path, line, character);
        parameters["newName"] = newName;
        return LspRename.ParseWorkspaceEdit(await RequestAsync("textDocument/rename", parameters).ConfigureAwait(false));
    }

    /// <summary>Tells the server that files it doesn't have open changed on disk.</summary>
    public void FilesChanged(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            if (!_initialized) return;
            _connection?.Notify("workspace/didChangeWatchedFiles", new JsonObject
            {
                ["changes"] = new JsonArray([.. paths.Select(p => (JsonNode)new JsonObject { ["uri"] = LspLocations.ToUri(p), ["type"] = 2 })]),
            });
        }
    }

    /// <summary>The types in the folder whose names match <paramref name="query"/>, as the server matches them.</summary>
    public async Task<IReadOnlyList<TypeSymbol>> TypesAsync(string query, CancellationToken cancellationToken = default) =>
        LspTypes.Parse(await RequestAsync("workspace/symbol", new JsonObject { ["query"] = query }, cancellationToken).ConfigureAwait(false));

    private async Task<JsonNode?> RequestAsync(string method, JsonObject parameters, CancellationToken cancellationToken = default)
    {
        JsonRpcConnection? connection;
        lock (_gate) connection = State == LanguageServerState.Ready ? _connection : null;
        return connection is null ? null : await connection.RequestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
    }

    private JsonObject Position(string path, int line, int character) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = UriOf(path) },
        ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
    };

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
            // Stopped before the kill, whose exit calls back in here, so that call can't replace the reason.
            State = LanguageServerState.Stopped;
        }
        process?.Kill();
        _post(() => StateChanged?.Invoke(this, EventArgs.Empty));
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
