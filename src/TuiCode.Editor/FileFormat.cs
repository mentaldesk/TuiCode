using TuiCode.Abstractions;

namespace TuiCode.Editor;

public enum IndentSource
{
    Settings,
    Detected,
    EditorConfig,
}

/// <summary>A file's own indentation, guessed from its indented lines (#622): tabs, or the step its spaces mostly use.</summary>
public sealed record IndentGuess(bool InsertSpaces, int Size)
{
    private const int MaxLines = 10_000;

    /// <summary>Null when nothing is indented, or the spaces never step by 2 to 8.</summary>
    public static IndentGuess? Of(IReadOnlyList<string> lines)
    {
        var tabLines = 0;
        var spaceLines = 0;
        var steps = new int[EditorSettings.MaxIndentSize + 1];
        var previous = 0;
        foreach (var line in lines.Take(MaxLines))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var spaces = 0;
            while (line[spaces] == ' ') spaces++;
            if (line[spaces] == '\t')
            {
                tabLines++;
                continue;
            }
            if (spaces > 0) spaceLines++;
            // A step of 1 is usually a block comment's ` * `, not the file's indentation.
            var step = Math.Abs(spaces - previous);
            if (step is >= 2 and <= EditorSettings.MaxIndentSize) steps[step]++;
            previous = spaces;
        }

        if (tabLines > spaceLines) return new IndentGuess(InsertSpaces: false, Size: 0);
        var best = 0;
        for (var size = 2; size < steps.Length; size++)
            if (steps[size] > steps[best]) best = size;
        return best == 0 ? null : new IndentGuess(InsertSpaces: true, Size: best);
    }
}

/// <summary>How one file indents and saves (#622): its <c>.editorconfig</c> first, then a guess from its contents, then Settings.</summary>
public sealed record FileFormat(
    int IndentSize,
    bool InsertSpaces,
    IndentSource IndentFrom,
    LineEnding LineEnding,
    bool InsertFinalNewline,
    bool FinalNewlineFromEditorConfig)
{
    public static FileFormat Of(EditorSettings settings, EditorConfigRules rules, IndentGuess? guess)
    {
        if (!settings.DetectIndentation) guess = null;
        var insertSpaces = rules.InsertSpaces ?? guess?.InsertSpaces ?? settings.InsertSpaces;
        var configured = insertSpaces ? rules.IndentSize ?? rules.TabWidth : rules.TabWidth ?? rules.IndentSize;
        var guessed = guess is { InsertSpaces: true } g && insertSpaces ? g.Size : (int?)null;
        return new FileFormat(
            configured ?? guessed ?? settings.IndentSize,
            insertSpaces,
            rules.SetsIndentation ? IndentSource.EditorConfig : guess is not null ? IndentSource.Detected : IndentSource.Settings,
            rules.LineEnding ?? settings.LineEnding,
            rules.InsertFinalNewline ?? settings.InsertFinalNewline,
            rules.InsertFinalNewline is not null);
    }

    /// <summary>What Document info says about the indentation: <c>2 spaces (detected)</c>, <c>tabs (.editorconfig)</c>.</summary>
    public string IndentDescription
    {
        get
        {
            var what = InsertSpaces ? (IndentSize == 1 ? "1 space" : $"{IndentSize} spaces") : "tabs";
            var from = IndentFrom switch
            {
                IndentSource.EditorConfig => EditorConfig.FileName,
                IndentSource.Detected => "detected",
                _ => "Settings",
            };
            return $"{what} ({from})";
        }
    }

    /// <summary>The final-newline rule, only when <c>.editorconfig</c> sets it.</summary>
    public string? FinalNewlineDescription => FinalNewlineFromEditorConfig
        ? $"{(InsertFinalNewline ? "final newline" : "no final newline")} ({EditorConfig.FileName})"
        : null;
}
