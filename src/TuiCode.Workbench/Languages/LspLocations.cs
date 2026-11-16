using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TuiCode.Workbench.Languages;

/// <summary>A place in a file: 0-based line, and the column in UTF-16 chars, as LSP counts them.</summary>
public readonly record struct SourceLocation(string Path, int Line, int Character);

public static partial class LspLocations
{
    public static string ToUri(string path) => IsDecompiled(path) ? path : new Uri(path).AbsoluteUri;

    /// <summary>Whether <paramref name="path"/> is csharp-ls's <c>csharp:/…/decompiled/…</c> URI for a library type, kept as it came (#610).</summary>
    public static bool IsDecompiled(string path) =>
        path.StartsWith("csharp:/", StringComparison.Ordinal) && path.Contains("/decompiled/", StringComparison.Ordinal);

    /// <summary>Compares paths as the file system does: ignoring case on Windows, where servers lower-case the drive letter.</summary>
    public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string? ToPath(string uri) =>
        Uri.TryCreate(EscapedDrive().Replace(uri, "file:///$1:", 1), UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;

    // vscode-uri, which TypeScript and Pyright use, escapes the drive's colon: file:///c%3A/...
    [GeneratedRegex("^file:///([A-Za-z])%3[Aa]")]
    private static partial Regex EscapedDrive();

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
        if (uri is null || (IsDecompiled(uri) ? uri : ToPath(uri)) is not { } path || start is null) return null;
        return new SourceLocation(path, start["line"]?.GetValue<int>() ?? 0, start["character"]?.GetValue<int>() ?? 0);
    }
}
