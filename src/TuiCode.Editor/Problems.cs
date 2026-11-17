using Terminal.Gui.Configuration;
using TuiCode.Abstractions;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Editor;

/// <summary>LSP's <c>DiagnosticSeverity</c>; lower is worse.</summary>
public enum ProblemSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
}

/// <summary>A language server's error, warning or info, over 0-based lines and UTF-16 columns, end exclusive.</summary>
public sealed record Problem(int StartLine, int StartCharacter, int EndLine, int EndCharacter, ProblemSeverity Severity, string? Code, string Message)
{
    public string Mark => Severity switch
    {
        ProblemSeverity.Error => "✖",
        ProblemSeverity.Warning => "⚠",
        _ => "ℹ",
    };

    /// <summary>How Quick info heads it: <c>✖ CS0103 The name 'nam' does not exist…</c>.</summary>
    public string Headline => $"{Mark} {Summary}";

    public string Summary => Code is { Length: > 0 } ? $"{Code} {Message}" : Message;

    /// <summary>Whether a caret at <paramref name="line"/> and UTF-16 <paramref name="character"/> is on it, either end included.</summary>
    public bool Covers(int line, int character) =>
        (line, character).CompareTo((StartLine, StartCharacter)) >= 0 && (line, character).CompareTo((EndLine, EndCharacter)) <= 0;
}

/// <summary>A problem's stretch of one line, in UTF-16 columns, end exclusive.</summary>
public readonly record struct ProblemSpan(int Start, int End, ProblemSeverity Severity);

/// <summary>A line's problems: the worst, which colours its number, and the stretches to underline.</summary>
public sealed record ProblemLine(ProblemSeverity Worst, IReadOnlyList<ProblemSpan> Spans);

public static class ProblemLines
{
    /// <summary>Each problem split over the lines it covers, keyed by line. A range ending at the start of a line leaves that line out.</summary>
    public static IReadOnlyDictionary<int, ProblemLine> Of(IEnumerable<Problem> problems)
    {
        var spans = new Dictionary<int, List<ProblemSpan>>();
        foreach (var problem in problems)
        {
            for (var line = problem.StartLine; line <= problem.EndLine; line++)
            {
                if (line > problem.StartLine && line == problem.EndLine && problem.EndCharacter == 0) break;
                var start = line == problem.StartLine ? problem.StartCharacter : 0;
                var end = line == problem.EndLine ? problem.EndCharacter : int.MaxValue;
                if (!spans.TryGetValue(line, out var list)) spans[line] = list = [];
                list.Add(new ProblemSpan(start, end, problem.Severity));
            }
        }
        return spans.ToDictionary(p => p.Key, p => new ProblemLine(p.Value.Min(s => s.Severity), p.Value));
    }

    /// <summary>
    /// The worst problem over the UTF-16 chars [<paramref name="start"/>, <paramref name="end"/>) of a line <paramref name="length"/> long,
    /// or null. An empty span marks the grapheme it starts at, and one at or past the end marks the last.
    /// </summary>
    public static ProblemSeverity? At(ProblemLine line, int start, int end, int length)
    {
        ProblemSeverity? worst = null;
        foreach (var span in line.Spans)
        {
            var from = Math.Min(span.Start, Math.Max(length - 1, 0));
            var to = Math.Max(Math.Min(span.End, length), from + 1);
            if (from < end && to > start && (worst is null || span.Severity < worst)) worst = span.Severity;
        }
        return worst;
    }
}

/// <summary>The colours problems are drawn in: the token theme's, else the UI theme's <c>Error</c>, <c>Warning</c> and <c>Accent</c> schemes.</summary>
public sealed record ProblemColours(Color Error, Color Warning, Color Information)
{
    private static readonly Color DefaultError = new(0xF8, 0x51, 0x49);
    private static readonly Color DefaultInformation = new(0x38, 0x8B, 0xFD);

    public const string ErrorKey = "editorError.foreground";
    public const string WarningKey = "editorWarning.foreground";
    public const string InformationKey = "editorInfo.foreground";

    /// <param name="themeColour">A token theme colour by key, or null.</param>
    /// <param name="schemeColour">A UI theme scheme's foreground by name, or null.</param>
    public static ProblemColours Resolve(Func<string, Color?> themeColour, Func<string, Color?> schemeColour)
    {
        var error = themeColour(ErrorKey) ?? schemeColour("Error") ?? DefaultError;
        return new ProblemColours(
            error,
            themeColour(WarningKey) ?? schemeColour(WarningColour.SchemeName) ?? error,
            themeColour(InformationKey) ?? schemeColour("Accent") ?? DefaultInformation);
    }

    public static ProblemColours For(SyntaxHighlighter? syntax) => Resolve(
        key => syntax?.EditorColors.TryGetValue(key, out var hex) == true && Color.TryParse(hex, out Color? color) ? color : null,
        name => SchemeManager.TryGetScheme(name, out var scheme) ? scheme.Normal.Foreground : null);

    public Color Of(ProblemSeverity severity) => severity switch
    {
        ProblemSeverity.Error => Error,
        ProblemSeverity.Warning => Warning,
        _ => Information,
    };

    public Attribute Underline(Attribute over, ProblemSeverity severity) =>
        over with { Foreground = Of(severity), Style = over.Style | TextStyle.Underline };
}
