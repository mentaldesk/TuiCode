namespace TuiCode.Abstractions;

/// <summary>How the editor indents and how it writes files on save (#14).</summary>
public sealed record EditorSettings
{
    public const int MinIndentSize = 1;
    public const int MaxIndentSize = 8;

    public static EditorSettings Default { get; } = new();

    /// <summary>Columns per indent level, and the display width of a tab.</summary>
    public int IndentSize { get; init; } = 4;

    /// <summary>Whether Tab inserts spaces rather than a tab character.</summary>
    public bool InsertSpaces { get; init; } = true;

    public LineEnding LineEnding { get; init; } = LineEnding.Auto;

    /// <summary>Whether saving a non-empty file makes sure it ends with a line break.</summary>
    public bool InsertFinalNewline { get; init; } = true;

    /// <summary>Whether a tab opens wrapped (#380); <c>Ctrl+T W</c> still flips each tab on its own.</summary>
    public bool WordWrap { get; init; }

    /// <summary>Languages that wrap whatever <see cref="WordWrap"/> says, keyed by grammar id (#381).</summary>
    public static IReadOnlyDictionary<string, bool> DefaultWrapByLanguage { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["markdown"] = true };

    /// <summary>The user's per-language wrap, over <see cref="DefaultWrapByLanguage"/>, keyed by grammar id.</summary>
    public IReadOnlyDictionary<string, bool> WrapByLanguage { get; init; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a tab whose grammar is <paramref name="languageId"/> opens wrapped.</summary>
    public bool WrapsLanguage(string? languageId) =>
        languageId is null ? WordWrap
        : WrapByLanguage.TryGetValue(languageId, out var wrap) ? wrap
        : DefaultWrapByLanguage.TryGetValue(languageId, out var builtIn) ? builtIn
        : WordWrap;

    public bool Equals(EditorSettings? other) =>
        other is not null
        && IndentSize == other.IndentSize
        && InsertSpaces == other.InsertSpaces
        && LineEnding == other.LineEnding
        && InsertFinalNewline == other.InsertFinalNewline
        && WordWrap == other.WordWrap
        && WrapByLanguage.Count == other.WrapByLanguage.Count
        && WrapByLanguage.All(w => other.WrapByLanguage.TryGetValue(w.Key, out var v) && v == w.Value);

    public override int GetHashCode() =>
        HashCode.Combine(IndentSize, InsertSpaces, LineEnding, InsertFinalNewline, WordWrap, WrapByLanguage.Count);
}

public enum LineEnding
{
    /// <summary>Keep each file's own line endings; a new, empty file takes the OS default.</summary>
    Auto,

    LF,

    CRLF,
}
