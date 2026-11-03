using System.Text.Json.Nodes;
using TuiCode.Syntax;

namespace TuiCode.Workbench.Languages;

/// <summary>A type a language server found in the folder: its name, kind, namespace or container, and where it's declared.</summary>
public sealed record TypeSymbol(string Name, SymbolKind Kind, string? Container, SourceLocation Location);

public static class LspTypes
{
    /// <summary>The classes, interfaces, structs and enums in a workspace/symbol result, of either shape, in the server's order.</summary>
    public static IReadOnlyList<TypeSymbol> Parse(JsonNode? result)
    {
        if (result is not JsonArray symbols) return [];
        var types = new List<TypeSymbol>();
        foreach (var symbol in symbols.OfType<JsonObject>())
            if (Read(symbol) is { } type) types.Add(type);
        return types;
    }

    // LSP's SymbolKind: servers give a record Class or Struct.
    private static SymbolKind? KindOf(int kind) => kind switch
    {
        5 => SymbolKind.Class,
        10 => SymbolKind.Enum,
        11 => SymbolKind.Interface,
        23 => SymbolKind.Struct,
        _ => null,
    };

    private static TypeSymbol? Read(JsonObject symbol)
    {
        if (KindOf(symbol["kind"]?.GetValue<int>() ?? 0) is not { } kind) return null;
        if (symbol["name"]?.GetValue<string>() is not { Length: > 0 } name) return null;
        if (symbol["location"]?["uri"]?.GetValue<string>() is not { } uri || LspLocations.ToPath(uri) is not { } path) return null;
        // A WorkspaceSymbol may leave out the range, to be resolved later; the top of the file is near enough.
        var start = symbol["location"]?["range"]?["start"];
        var location = new SourceLocation(path, start?["line"]?.GetValue<int>() ?? 0, start?["character"]?.GetValue<int>() ?? 0);
        var container = symbol["containerName"]?.GetValue<string>();
        return new TypeSymbol(name, kind, string.IsNullOrEmpty(container) ? null : container, location);
    }
}
