using System.Text.Json.Nodes;

namespace TuiCode.Workbench.Languages;

/// <summary>A place in a file: 0-based line, and the column in UTF-16 chars, as LSP counts them.</summary>
public readonly record struct SourceLocation(string Path, int Line, int Character);

public static class LspLocations
{
    public static string ToUri(string path) => new Uri(path).AbsoluteUri;

    public static string? ToPath(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;

    /// <summary>A definition result, whichever of its shapes: a Location, Locations, LocationLinks or null. Duplicates go.</summary>
    public static IReadOnlyList<SourceLocation> Parse(JsonNode? result)
    {
        var nodes = result switch
        {
            JsonArray array => array.ToList(),
            JsonObject one => [one],
            _ => [],
        };
        var locations = new List<SourceLocation>();
        foreach (var node in nodes)
            if (Read(node) is { } location && !locations.Contains(location))
                locations.Add(location);
        return locations;
    }

    private static SourceLocation? Read(JsonNode? node)
    {
        if (node is not JsonObject location) return null;
        var uri = (location["targetUri"] ?? location["uri"])?.GetValue<string>();
        var start = (location["targetSelectionRange"] ?? location["targetRange"] ?? location["range"])?["start"];
        if (uri is null || ToPath(uri) is not { } path || start is null) return null;
        return new SourceLocation(path, start["line"]?.GetValue<int>() ?? 0, start["character"]?.GetValue<int>() ?? 0);
    }
}
