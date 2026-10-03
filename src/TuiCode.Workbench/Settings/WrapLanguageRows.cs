using TuiCode.Syntax;

namespace TuiCode.Workbench.Settings;

internal enum WrapLanguageKind
{
    Default,
    Changed,
    Added,
    /// <summary>Offers to add a language that isn't listed.</summary>
    Add,
}

internal sealed record WrapLanguageRow(string Id, string Name, bool Wrap, bool? DefaultWrap, WrapLanguageKind Kind)
{
    public string Display => Kind switch
    {
        WrapLanguageKind.Add => $"Add \"{Name}\"…",
        WrapLanguageKind.Changed => $"{Name,-28}{OnOff(Wrap)}  (default: {OnOff(DefaultWrap ?? false)})",
        WrapLanguageKind.Added => $"{Name,-28}{OnOff(Wrap)}  (added)",
        _ => $"{Name,-28}{OnOff(Wrap)}  (default)",
    };

    private static string OnOff(bool wrap) => wrap ? "On" : "Off";
}

/// <summary>Settings → Editor's Wrap by language rows (#381): built-in overrides merged with the user's, filtered. TG-free for testing.</summary>
internal static class WrapLanguageRows
{
    public static IReadOnlyList<WrapLanguageRow> Build(
        IReadOnlyDictionary<string, bool> defaults,
        IReadOnlyDictionary<string, bool> overrides,
        IEnumerable<SyntaxLanguage> languages,
        string filter)
    {
        var names = languages
            .GroupBy(l => l.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        string NameOf(string id) => names.GetValueOrDefault(id) ?? $"{id} (unknown)";

        var rows = new List<WrapLanguageRow>();
        foreach (var (id, wrap) in defaults)
        {
            rows.Add(overrides.TryGetValue(id, out var changed)
                ? new WrapLanguageRow(id, NameOf(id), changed, wrap, WrapLanguageKind.Changed)
                : new WrapLanguageRow(id, NameOf(id), wrap, wrap, WrapLanguageKind.Default));
        }
        foreach (var (id, wrap) in overrides)
        {
            if (!defaults.ContainsKey(id))
                rows.Add(new WrapLanguageRow(id, NameOf(id), wrap, null, WrapLanguageKind.Added));
        }

        filter = filter.Trim();
        var visible = rows
            .Where(r => Matches(r.Name, r.Id, filter))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (filter.Length == 0) return visible;

        var listed = rows.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        visible.AddRange(names
            .Where(n => !listed.Contains(n.Key) && Matches(n.Value, n.Key, filter))
            .OrderBy(n => !string.Equals(n.Value, filter, StringComparison.OrdinalIgnoreCase))
            .ThenBy(n => n.Value, StringComparer.OrdinalIgnoreCase)
            .Select(n => new WrapLanguageRow(n.Key, n.Value, true, null, WrapLanguageKind.Add)));
        return visible;
    }

    /// <summary>The override after Enter: On → Off → Default, skipping a value that's the language's default anyway.</summary>
    public static bool? Next(bool? current, bool? defaultWrap)
    {
        bool? next = current switch
        {
            null => defaultWrap is { } wrap ? !wrap : true,
            true => false,
            false => null,
        };
        return next == defaultWrap ? null : next;
    }

    private static bool Matches(string name, string id, string filter) =>
        name.Contains(filter, StringComparison.OrdinalIgnoreCase) || id.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
