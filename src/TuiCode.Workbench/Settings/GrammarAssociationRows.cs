using TuiCode.Syntax;

namespace TuiCode.Workbench.Settings;

internal enum GrammarAssociationKind
{
    Default,
    Changed,
    Added,
    /// <summary>Offers to add the filter text as a new pattern.</summary>
    Add,
}

internal sealed record GrammarAssociationRow(string Pattern, string GrammarName, string? DefaultName, GrammarAssociationKind Kind)
{
    public string Display => Kind switch
    {
        GrammarAssociationKind.Add => $"Add \"{Pattern}\"…",
        GrammarAssociationKind.Changed => $"{Pattern,-28}{GrammarName}  (default: {DefaultName})",
        GrammarAssociationKind.Added => $"{Pattern,-28}{GrammarName}  (added)",
        _ => $"{Pattern,-28}{GrammarName}",
    };
}

/// <summary>The Grammars settings pane's rows (#21): built-in associations merged with the user's, filtered. TG-free for testing.</summary>
internal static class GrammarAssociationRows
{
    public static IReadOnlyList<GrammarAssociationRow> Build(
        IReadOnlyDictionary<string, SyntaxLanguage> defaults,
        IReadOnlyDictionary<string, string> associations,
        Func<string, SyntaxLanguage?> grammarById,
        string filter)
    {
        var rows = new List<GrammarAssociationRow>();
        foreach (var (pattern, grammar) in defaults)
        {
            rows.Add(associations.TryGetValue(pattern, out var id)
                ? new GrammarAssociationRow(pattern, NameOf(id, grammarById), grammar.Name, GrammarAssociationKind.Changed)
                : new GrammarAssociationRow(pattern, grammar.Name, null, GrammarAssociationKind.Default));
        }
        foreach (var (pattern, id) in associations)
        {
            if (!defaults.ContainsKey(pattern))
                rows.Add(new GrammarAssociationRow(pattern, NameOf(id, grammarById), null, GrammarAssociationKind.Added));
        }

        filter = filter.Trim();
        var visible = rows
            .Where(r => r.Pattern.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || r.GrammarName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Pattern, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A bare word is more likely a search for a grammar name ("Python") than a file name, unless nothing matches it.
        if (IsPattern(filter) && (filter.StartsWith('.') || visible.Count == 0)
            && !rows.Any(r => string.Equals(r.Pattern, filter, StringComparison.OrdinalIgnoreCase)))
            visible.Insert(0, new GrammarAssociationRow(filter, "", null, GrammarAssociationKind.Add));
        return visible;
    }

    /// <summary>An extension like <c>.tfvars</c> or an exact file name like <c>Jenkinsfile</c>.</summary>
    internal static bool IsPattern(string text) =>
        text.Length > 0 && !text.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '*' or '?');

    private static string NameOf(string id, Func<string, SyntaxLanguage?> grammarById) =>
        id == SyntaxHighlighter.PlainText ? Workbench.PlainTextName : grammarById(id)?.Name ?? $"{id} (unknown)";
}
