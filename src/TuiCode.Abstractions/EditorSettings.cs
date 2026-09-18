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
}

public enum LineEnding
{
    /// <summary>Keep each file's own line endings; a new, empty file takes the OS default.</summary>
    Auto,

    LF,

    CRLF,
}
