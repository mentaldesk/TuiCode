using System.Text.Json.Nodes;

namespace TuiCode.Workbench.Languages;

/// <summary>A replacement of the text between two places, as LSP counts them: 0-based lines and UTF-16 columns.</summary>
public readonly record struct TextEdit(int StartLine, int StartCharacter, int EndLine, int EndCharacter, string NewText);

/// <summary>The edits a workspace edit makes to one file.</summary>
public sealed record FileEdit(string Path, IReadOnlyList<TextEdit> Edits);

/// <summary>What <c>textDocument/prepareRename</c> said: the name's range, and the name to offer when the server gives one.</summary>
public sealed record RenameTarget(TextEdit? Range, string? Placeholder);

public static class LspRename
{
    /// <summary>A <c>prepareRename</c> answer, or null when the server says the place can't be renamed.</summary>
    public static RenameTarget? ParsePrepare(JsonNode? result)
    {
        if (result is not JsonObject answer) return null;
        if (answer["defaultBehavior"] is not null) return new RenameTarget(null, null);
        if (answer["range"] is JsonObject range) return new RenameTarget(Range(range), answer["placeholder"]?.GetValue<string>());
        return answer["start"] is not null ? new RenameTarget(Range(answer), null) : null;
    }

    /// <summary>The text edits in a <c>WorkspaceEdit</c>, from <c>documentChanges</c> if it has them, else <c>changes</c>; file operations are left out.</summary>
    public static IReadOnlyList<FileEdit> ParseWorkspaceEdit(JsonNode? result)
    {
        var byPath = new Dictionary<string, List<TextEdit>>(LspLocations.PathComparer);
        var order = new List<string>();
        void Add(string? uri, JsonNode? edits)
        {
            if (uri is null || LspLocations.ToPath(uri) is not { } path || edits is not JsonArray array) return;
            if (!byPath.TryGetValue(path, out var list))
            {
                byPath[path] = list = [];
                order.Add(path);
            }
            foreach (var edit in array.OfType<JsonObject>())
                if (edit["range"] is JsonObject range && edit["newText"]?.GetValue<string>() is { } text)
                    list.Add(Range(range) with { NewText = text });
        }

        if (result?["documentChanges"] is JsonArray documentChanges)
        {
            foreach (var change in documentChanges.OfType<JsonObject>())
                if (change["kind"] is null)
                    Add(change["textDocument"]?["uri"]?.GetValue<string>(), change["edits"]);
        }
        else if (result?["changes"] is JsonObject changes)
        {
            foreach (var (uri, edits) in changes)
                Add(uri, edits);
        }
        return [.. order.Where(p => byPath[p].Count > 0).Select(p => new FileEdit(p, byPath[p]))];
    }

    private static TextEdit Range(JsonObject range) => new(
        range["start"]?["line"]?.GetValue<int>() ?? 0,
        range["start"]?["character"]?.GetValue<int>() ?? 0,
        range["end"]?["line"]?.GetValue<int>() ?? 0,
        range["end"]?["character"]?.GetValue<int>() ?? 0,
        "");
}
