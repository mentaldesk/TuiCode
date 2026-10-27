using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

/// <summary>
/// Launches in-process fake language servers. Each keeps the documents it's told about, and answers a definition
/// with every declaration (<c>class X</c>, <c>void X</c>, ...) of the word at the position, in those documents and
/// in <see cref="Files"/>. References are every other whole-word use of it, and document symbols the file's first class
/// with its methods.
/// </summary>
internal sealed partial class FakeLanguageServer : ILanguageServerLauncher
{
    private readonly ConcurrentQueue<(string Method, JsonNode? Params)> _received = new();

    /// <summary>Not installed: every launch fails.</summary>
    public bool Missing { get; set; }

    /// <summary>Whether it reports loading the solution as work-done progress, as csharp-ls does.</summary>
    public bool ReportsProgress { get; set; } = true;

    /// <summary>Leaves the server loading until <see cref="FinishLoading"/>.</summary>
    public bool HoldLoading { get; set; }

    /// <summary>Answers initialize with this error.</summary>
    public string? InitializeError { get; set; }

    /// <summary>Holds the answer to initialize until it's set.</summary>
    public ManualResetEventSlim? HoldInitialize { get; set; }

    /// <summary>Reports loading, start to finish, before it has answered initialize.</summary>
    public bool LoadsWhileInitializing { get; set; }

    /// <summary>Holds every references request until it's set.</summary>
    public ManualResetEventSlim? HoldReferences { get; set; }

    /// <summary>Holds hover requests on <see cref="HoldHoverOf"/> until it's set, while answering the rest.</summary>
    public ManualResetEventSlim? HoldHover { get; set; }

    public string? HoldHoverOf { get; set; }

    /// <summary>What hover answers for a word; by default its declaration as C# and a line about it, or null for no declaration.</summary>
    public Func<string, string?, JsonNode?>? Hover { get; set; }

    /// <summary>Files the server has loaded from the solution, by path.</summary>
    public ConcurrentDictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);

    /// <summary>Library types, by csharp-ls's <c>csharp:/…/decompiled/…</c> URI: definitions reach their declarations, and <c>csharp/metadata</c> gives their source.</summary>
    public ConcurrentDictionary<string, DecompiledSource> Library { get; } = new(StringComparer.Ordinal);

    /// <summary>Library URIs whose <c>csharp/metadata</c> answers null, as when csharp-ls can't decompile them.</summary>
    public HashSet<string> Undecompilable { get; } = new(StringComparer.Ordinal);

    public List<(string Command, string Directory)> Launches { get; } = [];

    public List<InProcessServer> Servers { get; } = [];

    public InProcessServer Current => Servers[^1];

    public IReadOnlyList<string> Methods => [.. _received.Select(m => m.Method)];

    public IEnumerable<JsonNode?> Received(string method) => _received.Where(m => m.Method == method).Select(m => m.Params);

    public ILanguageServerProcess? Launch(string command, IReadOnlyList<string> arguments, string directory)
    {
        Launches.Add((command, directory));
        if (Missing) return null;
        var server = new InProcessServer();
        server.Connection.RequestHandler = (method, parameters) => Answer(server, method, parameters);
        if (HoldHover is { } hold)
            server.Connection.AsyncRequestHandler = (method, parameters) =>
                method == "textDocument/hover" && WordAt(parameters!) == HoldHoverOf
                    ? Task.Run(() =>
                    {
                        hold.Wait(TimeSpan.FromSeconds(30));
                        return Answer(server, method, parameters);
                    })
                    : Task.FromResult(Answer(server, method, parameters));
        server.Connection.NotificationReceived += (method, parameters) => OnNotification(server, method, parameters);
        server.Connection.Start();
        Servers.Add(server);
        return server;
    }

    public void FinishLoading() => Progress(Current, "end");

    /// <summary>The server process dies.</summary>
    public void Crash() => Current.Exit();

    private void Progress(InProcessServer server, string kind) =>
        server.Connection.Notify("$/progress", new JsonObject { ["token"] = "load", ["value"] = new JsonObject { ["kind"] = kind } });

    private JsonNode? Answer(InProcessServer server, string method, JsonNode? parameters)
    {
        _received.Enqueue((method, parameters?.DeepClone()));
        if (method == "initialize")
        {
            HoldInitialize?.Wait(TimeSpan.FromSeconds(30));
            if (LoadsWhileInitializing)
            {
                Progress(server, "begin");
                Progress(server, "end");
            }
        }
        return method switch
        {
            "initialize" when InitializeError is { } error => throw new JsonRpcException(-32603, error),
            "initialize" => new JsonObject { ["capabilities"] = new JsonObject { ["definitionProvider"] = true } },
            "textDocument/definition" => Definitions(parameters!),
            "textDocument/references" => References(parameters!),
            "textDocument/documentSymbol" => Symbols(parameters!),
            "textDocument/hover" => HoverAt(parameters!),
            "csharp/metadata" => Metadata(parameters!),
            _ => null,
        };
    }

    private void OnNotification(InProcessServer server, string method, JsonNode? parameters)
    {
        var document = parameters?["textDocument"];
        switch (method)
        {
            case "initialized" when ReportsProgress:
                Progress(server, "begin");
                if (!HoldLoading) Progress(server, "end");
                break;
            case "textDocument/didOpen":
                Documents[Path(document!)] = document!["text"]!.GetValue<string>();
                break;
            case "textDocument/didChange":
                Documents[Path(document!)] = parameters!["contentChanges"]![0]!["text"]!.GetValue<string>();
                break;
            case "textDocument/didClose":
                Documents.TryRemove(Path(document!), out _);
                break;
            case "exit":
                server.Exit();
                break;
        }
        // Recorded once handled, so a test that has seen it can rely on what it did.
        _received.Enqueue((method, parameters?.DeepClone()));
    }

    private JsonArray Definitions(JsonNode parameters)
    {
        var path = Path(parameters["textDocument"]!);
        var line = parameters["position"]!["line"]!.GetValue<int>();
        var character = parameters["position"]!["character"]!.GetValue<int>();
        var lines = Lines(Sources().GetValueOrDefault(path) ?? "");
        if (line >= lines.Length || Word(lines[line], character) is not { } word) return [];

        var found = new JsonArray();
        foreach (var (file, text) in Sources().OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var fileLines = Lines(text);
            for (var row = 0; row < fileLines.Length; row++)
                foreach (Match match in Regex.Matches(fileLines[row], $@"\b(?:class|struct|interface|void|int|string|var)\s+({Regex.Escape(word)})\b"))
                    found.Add(new JsonObject
                    {
                        ["uri"] = LspLocations.ToUri(file),
                        ["range"] = new JsonObject
                        {
                            ["start"] = new JsonObject { ["line"] = row, ["character"] = match.Groups[1].Index },
                            ["end"] = new JsonObject { ["line"] = row, ["character"] = match.Groups[1].Index + word.Length },
                        },
                    });
        }
        return found;
    }

    private JsonObject? Metadata(JsonNode parameters)
    {
        var uri = parameters["textDocument"]!["uri"]!.GetValue<string>();
        if (Undecompilable.Contains(uri) || !Library.TryGetValue(uri, out var source)) return null;
        return new JsonObject
        {
            ["projectName"] = "work",
            ["assemblyName"] = source.AssemblyName,
            ["symbolName"] = source.SymbolName,
            ["source"] = source.Source,
        };
    }

    private JsonArray References(JsonNode parameters)
    {
        HoldReferences?.Wait(TimeSpan.FromSeconds(30));
        if (WordAt(parameters) is not { } word) return [];
        var found = new JsonArray();
        foreach (var (file, text) in Sources().OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var fileLines = Lines(text);
            for (var row = 0; row < fileLines.Length; row++)
            {
                var declared = Regex.Matches(fileLines[row], $@"\b(?:class|struct|interface|void|int|string|var)\s+({Regex.Escape(word)})\b")
                    .Select(m => m.Groups[1].Index).ToHashSet();
                foreach (Match match in Regex.Matches(fileLines[row], $@"\b{Regex.Escape(word)}\b"))
                    if (!declared.Contains(match.Index)) found.Add(Location(file, row, match.Index, word.Length));
            }
        }
        return found;
    }

    private JsonNode? HoverAt(JsonNode parameters)
    {
        if (WordAt(parameters) is not { } word) return null;
        var declaration = Sources().Values.SelectMany(Lines).Select(l => l.Trim())
            .FirstOrDefault(l => Regex.IsMatch(l, $@"\b(?:class|struct|interface|void|int|string)\s+{Regex.Escape(word)}\b"));
        if (Hover is { } hover) return hover(word, declaration);
        if (declaration is null) return null;
        return new JsonObject
        {
            ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = $"```csharp\n{declaration}\n```\n\nAbout {word}." },
        };
    }

    private JsonArray Symbols(JsonNode parameters)
    {
        var lines = Lines(Sources().GetValueOrDefault(Path(parameters["textDocument"]!)) ?? "");
        JsonObject? type = null;
        var members = new JsonArray();
        for (var row = 0; row < lines.Length; row++)
        {
            if (type is null && Regex.Match(lines[row], @"\bclass\s+(\w+)") is { Success: true } declaration)
                type = Symbol(declaration.Groups[1].Value, 5, Range(0, 0, lines.Length, 0), Range(row, declaration.Groups[1].Index, row, declaration.Groups[1].Index + declaration.Groups[1].Length));
            else if (type is not null && Regex.Match(lines[row], @"\b(?:void|int|string)\s+(\w+)\(") is { Success: true } member)
                members.Add(Symbol($"{member.Groups[1].Value}()", 6, Range(row, 0, row + 1, 0), Range(row, member.Groups[1].Index, row, member.Groups[1].Index + member.Groups[1].Length)));
        }
        if (type is null) return [];
        type["children"] = members;
        return [type];
    }

    private static JsonObject Symbol(string name, int kind, JsonObject range, JsonObject selection) =>
        new() { ["name"] = name, ["kind"] = kind, ["range"] = range, ["selectionRange"] = selection };

    private static JsonObject Range(int startLine, int startCharacter, int endLine, int endCharacter) => new()
    {
        ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startCharacter },
        ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endCharacter },
    };

    private static JsonObject Location(string file, int row, int character, int length) =>
        new() { ["uri"] = LspLocations.ToUri(file), ["range"] = Range(row, character, row, character + length) };

    private string? WordAt(JsonNode parameters)
    {
        var line = parameters["position"]!["line"]!.GetValue<int>();
        var character = parameters["position"]!["character"]!.GetValue<int>();
        var lines = Lines(Sources().GetValueOrDefault(Path(parameters["textDocument"]!)) ?? "");
        return line < lines.Length ? Word(lines[line], character) : null;
    }

    private Dictionary<string, string> Sources()
    {
        var sources = Files.ToDictionary();
        foreach (var (uri, library) in Library) sources[uri] = library.Source;
        foreach (var (open, text) in Documents) sources[open] = text;
        return sources;
    }

    private static string[] Lines(string text) => text.ReplaceLineEndings("\n").Split('\n');

    private static string? Word(string line, int character)
    {
        var match = WordPattern().Matches(line).FirstOrDefault(m => m.Index <= character && character <= m.Index + m.Length);
        return match?.Value is { } word && !Keywords.Contains(word) ? word : null;
    }

    private static readonly HashSet<string> Keywords = ["public", "class", "void", "int", "string", "var", "return", "new", "partial"];

    private static string Path(JsonNode document) =>
        document["uri"]!.GetValue<string>() is var uri && LspLocations.IsDecompiled(uri) ? uri : LspLocations.ToPath(uri)!;

    [GeneratedRegex(@"\w+")]
    private static partial Regex WordPattern();
}
