using System.Text.RegularExpressions;

namespace TuiCode.Workbench.Languages;

public static partial class CSharpNamespace
{
    /// <summary>The first namespace a C# file declares, block or file-scoped, or null.</summary>
    public static string? Of(IEnumerable<string> lines) =>
        lines.Select(l => Declaration().Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value;

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)")]
    private static partial Regex Declaration();
}
