using System.Text.Json.Nodes;

namespace TuiCode.Workbench.Languages;

public static class LspSymbols
{
    // File, Module, Namespace and Package: what a member sits in, but not what names it.
    private static readonly HashSet<int> Containers = [1, 2, 3, 4];

    /// <summary>The symbol a documentSymbol result, of either shape, declares at a position: <c>LineDiff.Hunks</c>.</summary>
    public static string? QualifiedName(JsonNode? result, int line, int character)
    {
        if (result is not JsonArray symbols) return null;
        var path = new List<string>();
        if (FindDeclaration(symbols, line, character, path)) return Join(path);
        return FromInformation(symbols, line, character);
    }

    private static bool FindDeclaration(JsonArray symbols, int line, int character, List<string> path)
    {
        foreach (var symbol in symbols.OfType<JsonObject>())
        {
            if (symbol["selectionRange"] is null) continue;
            var named = !Containers.Contains(symbol["kind"]?.GetValue<int>() ?? 0) && Name(symbol) is not null;
            if (named) path.Add(Name(symbol)!);
            // csharp-ls gives its file symbol a selection spanning the whole file.
            if (named && Contains(symbol["selectionRange"], line, character)) return true;
            if (Contains(symbol["range"], line, character) && symbol["children"] is JsonArray children
                && FindDeclaration(children, line, character, path)) return true;
            if (named) path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    private static string? FromInformation(JsonArray symbols, int line, int character)
    {
        var symbol = symbols.OfType<JsonObject>()
            .Where(s => s["location"]?["range"]?["start"]?["line"]?.GetValue<int>() == line || Contains(s["location"]?["range"], line, character))
            .Where(s => !Containers.Contains(s["kind"]?.GetValue<int>() ?? 0))
            .OrderByDescending(s => s["location"]?["range"]?["start"]?["line"]?.GetValue<int>() ?? 0)
            .FirstOrDefault();
        if (symbol is null || Name(symbol) is not { } name) return null;
        var container = symbol["containerName"]?.GetValue<string>();
        return string.IsNullOrEmpty(container) ? name : Join([container[(container.LastIndexOf('.') + 1)..], name]);
    }

    // csharp-ls names a method with its parameters: Hunks(string before, string after).
    private static string? Name(JsonObject symbol) =>
        symbol["name"]?.GetValue<string>() is { Length: > 0 } name ? name.Split('(')[0].Trim() : null;

    private static string? Join(List<string> path) => path.Count == 0 ? null : string.Join('.', path.TakeLast(2));

    private static bool Contains(JsonNode? range, int line, int character)
    {
        if (range?["start"] is not { } start || range["end"] is not { } end) return false;
        var position = (line, character);
        return Compare(Point(start), position) <= 0 && Compare(position, Point(end)) <= 0;
    }

    private static (int Line, int Character) Point(JsonNode node) =>
        (node["line"]?.GetValue<int>() ?? 0, node["character"]?.GetValue<int>() ?? 0);

    private static int Compare((int Line, int Character) a, (int Line, int Character) b) =>
        a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Character.CompareTo(b.Character);
}
