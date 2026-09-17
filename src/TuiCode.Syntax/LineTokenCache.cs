using System.Diagnostics;
using TextMateSharp.Grammars;

namespace TuiCode.Syntax;

/// <summary>One buffer's TextMate tokens, lexed lazily top-down; after an edit, a line whose start state is unchanged keeps its tokens.</summary>
public sealed class LineTokenCache
{
    // VS Code's default editor.maxTokenizationLineLength: longer lines (minified files) stay plain rather than stall.
    public const int MaxLineLength = 20_000;

    // A line that overruns (e.g. while a cold grammar compiles its regexes) is re-lexed this many times before its partial tokens stand.
    internal const int MaxRetries = 2;

    private readonly IGrammar _grammar;
    private readonly List<Line> _lines = [];
    private int _valid;
    private int _retryFrom = int.MaxValue;
    private bool _failed;
    private int _themeVersion;

    internal LineTokenCache(SyntaxHighlighter highlighter, IGrammar grammar, SyntaxLanguage language)
    {
        Highlighter = highlighter;
        Language = language;
        _grammar = grammar;
        _themeVersion = highlighter.ThemeVersion;
    }

    public SyntaxHighlighter Highlighter { get; }

    public SyntaxLanguage Language { get; }

    internal int LinesLexed { get; private set; }

    internal TimeSpan LineTimeLimit { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Reconcile with the buffer's lines. Cheap when unchanged lines are the same string instances.</summary>
    public void Update(IReadOnlyList<string> lines)
    {
        var prefix = 0;
        while (prefix < lines.Count && prefix < _lines.Count && string.Equals(_lines[prefix].Text, lines[prefix]))
            prefix++;
        var suffix = 0;
        while (suffix < lines.Count - prefix && suffix < _lines.Count - prefix
               && string.Equals(_lines[_lines.Count - 1 - suffix].Text, lines[lines.Count - 1 - suffix]))
            suffix++;

        var fresh = lines.Count - prefix - suffix;
        _lines.RemoveRange(prefix, _lines.Count - prefix - suffix);
        _lines.InsertRange(prefix, Enumerable.Range(prefix, fresh).Select(i => new Line(lines[i])));
        _valid = Math.Min(_valid, prefix);
    }

    /// <summary>Lexes every line through <paramref name="row"/>, returning false if <paramref name="budget"/> ran out first or a line needs another try.</summary>
    public bool TokenizeThrough(int row, TimeSpan budget)
    {
        if (_failed) return true;
        SyncTheme();
        row = Math.Min(row, _lines.Count - 1);
        _valid = Math.Min(_valid, _retryFrom);
        _retryFrom = int.MaxValue;
        var clock = Stopwatch.StartNew();
        var lexed = 0;
        for (; _valid <= row; _valid++)
        {
            var line = _lines[_valid];
            var start = _valid == 0 ? null : _lines[_valid - 1].End;
            if (line.Tokens is not null && !line.Partial && Equals(line.Start, start))
                continue;
            if (lexed > 0 && clock.Elapsed >= budget)
                return false;
            if (!TryLex(line, start))
                return true;
            if (line.Partial)
                _retryFrom = Math.Min(_retryFrom, _valid);
            lexed++;
        }
        return _retryFrom == int.MaxValue;
    }

    /// <summary>(UTF-16 start, metadata) pairs, or null until the row is lexed.</summary>
    public int[]? TokensFor(int row)
    {
        if (_failed) return null;
        SyncTheme();
        return row < _valid ? _lines[row].Tokens : null;
    }

    private bool TryLex(Line line, IStateStack? start)
    {
        try
        {
            Lex(line, start);
            return true;
        }
        // A user grammar can be broken in ways that only show when a line is lexed (e.g. an invalid regex): stay plain.
        catch (Exception)
        {
            _failed = true;
            return false;
        }
    }

    private void Lex(Line line, IStateStack? start)
    {
        line.Start = start;
        LinesLexed++;
        if (line.Text.Length > MaxLineLength)
        {
            line.Tokens = [];
            line.End = start;
            return;
        }
        var clock = Stopwatch.StartNew();
        var result = _grammar.TokenizeLine2(line.Text, start, LineTimeLimit);
        line.Tokens = result.Tokens;
        line.End = result.RuleStack;
        // TextMateSharp's StoppedEarly is internal; it can only stop once the limit has passed, so elapsed time is a safe stand-in.
        line.Partial = clock.Elapsed >= LineTimeLimit && line.Retries++ < MaxRetries;
    }

    private void SyncTheme()
    {
        if (_themeVersion == Highlighter.ThemeVersion) return;
        _themeVersion = Highlighter.ThemeVersion;
        foreach (var line in _lines)
            line.Tokens = null;
        _valid = 0;
    }

    private sealed class Line(string text)
    {
        public string Text { get; } = text;
        public IStateStack? Start { get; set; }
        public IStateStack? End { get; set; }
        public int[]? Tokens { get; set; }
        public bool Partial { get; set; }
        public int Retries { get; set; }
    }
}
