using System.Text.Json;
using System.Text.Json.Nodes;
using TuiCode.Editor;

namespace TuiCode.Workbench.Languages;

/// <summary>A <c>textDocument/publishDiagnostics</c> notification: the file's problems, replacing any it had.</summary>
public sealed record FileProblems(string Path, IReadOnlyList<Problem> Problems);

public static class LspDiagnostics
{
    private const int Hint = 4;

    /// <summary>Its file and problems, hints left out; null when it names no file.</summary>
    public static FileProblems? Parse(JsonNode? parameters)
    {
        if (parameters?["uri"]?.GetValue<string>() is not { } uri || LspLocations.ToPath(uri) is not { } path) return null;
        var problems = new List<Problem>();
        foreach (var node in parameters["diagnostics"] as JsonArray ?? [])
            if (Read(node) is { } problem) problems.Add(problem);
        return new FileProblems(path, problems);
    }

    private static Problem? Read(JsonNode? node)
    {
        if (node is not JsonObject diagnostic || diagnostic["range"] is not JsonObject range) return null;
        // The protocol leaves a missing severity to the client; VS Code takes it as an error.
        var severity = Int(diagnostic["severity"]) ?? (int)ProblemSeverity.Error;
        if (severity is < 1 or >= Hint) return null;
        var (startLine, startCharacter) = Position(range["start"]);
        var (endLine, endCharacter) = Position(range["end"]);
        if ((endLine, endCharacter).CompareTo((startLine, startCharacter)) < 0) (endLine, endCharacter) = (startLine, startCharacter);
        var message = (diagnostic["message"] is JsonValue m && m.TryGetValue<string>(out var text) ? text : "").ReplaceLineEndings(" ").Trim();
        return new Problem(startLine, startCharacter, endLine, endCharacter, (ProblemSeverity)severity, Code(diagnostic["code"]), message);
    }

    private static (int Line, int Character) Position(JsonNode? position) =>
        (Math.Max(Int(position?["line"]) ?? 0, 0), Math.Max(Int(position?["character"]) ?? 0, 0));

    private static string? Code(JsonNode? code) => code is JsonValue value
        ? value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => value.ToJsonString(),
            _ => null,
        }
        : null;

    private static int? Int(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number) ? number : null;
}
