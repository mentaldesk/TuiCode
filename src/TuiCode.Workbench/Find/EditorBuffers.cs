using TuiCode.Editor;
using TuiCode.Search;

namespace TuiCode.Workbench.Find;

/// <summary>Exposes the editor's open tabs to workspace search, so dirty buffers are searched and replaced in place.</summary>
internal sealed class EditorBuffers(EditorGroup group) : IOpenBuffers
{
    public IReadOnlyDictionary<string, string> Snapshot() =>
        group.Tabs.ToDictionary(t => t.File.FullName, t => string.Join('\n', t.Lines), StringComparer.Ordinal);

    public int? ReplaceAll(string fullPath, string query, string replacement)
    {
        if (group.Tabs.FirstOrDefault(t => string.Equals(t.File.FullName, fullPath, StringComparison.Ordinal)) is not { } tab)
            return null;

        var matches = TextSearch.FindAll(tab.Lines, query);
        // Back to front so earlier coordinates stay valid.
        for (var i = matches.Count - 1; i >= 0; i--)
            tab.Replace(matches[i], replacement);
        return matches.Count;
    }
}
