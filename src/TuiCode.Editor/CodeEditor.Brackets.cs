using Terminal.Gui.Configuration;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

/// <summary>The bracket at the primary caret and its partner (#539).</summary>
internal sealed partial class CodeEditor
{
    private Point? _bracketsAt;
    private Point? _bracket;
    private Point? _partner;
    private bool _unmatched;
    private bool _bracketsLexing;
    private Color _bracketBackground;
    private Attribute _bracketError;

    /// <summary>The bracket the highlight is on and its partner, if found, in grapheme columns.</summary>
    internal (Point Bracket, Point? Partner, bool Unmatched)? BracketMatch =>
        _bracket is { } bracket ? (bracket, _partner, _unmatched) : null;

    /// <summary>Where Go to matching bracket goes: the partner of the bracket at the caret, or the one closing the block it's in.</summary>
    internal Point? MatchingBracket()
    {
        Syntax?.Update(_lines);
        var brackets = new Brackets(_lines, row => BracketTokens(row, TimeSpan.MaxValue));
        var caret = SpotOf(InsertionPoint);
        var target = brackets.At(caret) is { } pair ? pair.Partner : brackets.Closing(caret);
        return target is { } spot ? PointOf(spot) : null;
    }

    private void PrepareBrackets()
    {
        _bracketBackground = Syntax?.Highlighter.EditorColors.TryGetValue("editorBracketMatch.background", out var hex) == true
                             && Color.TryParse(hex, out Color? themed) && themed is { } color
            ? color
            : _highlight.Background;
        _bracketError = SchemeManager.TryGetScheme("Error", out var error) ? error.Active : new Attribute(Color.White, Color.Red);

        var caret = InsertionPoint;
        if (_bracketsAt == caret) return;
        _bracketsLexing = false;
        var pair = new Brackets(_lines, row => BracketTokens(row, SyntaxBudget)).At(SpotOf(caret));
        _bracket = pair is { GaveUp: false } ? PointOf(pair.Bracket) : null;
        _partner = pair?.Partner is { } partner ? PointOf(partner) : null;
        _unmatched = pair is { Partner: null, GaveUp: false };
        if (_bracketsLexing) DrawAgainSoon();
        else _bracketsAt = caret;
    }

    private void ForgetBrackets() => _bracketsAt = null;

    private Attribute? BracketAttribute(int col, int row)
    {
        var cell = new Point(col, row);
        if (cell != _bracket && cell != _partner) return null;
        return _unmatched ? _bracketError : _cellAttribute with { Background = _bracketBackground };
    }

    // Null while the row waits to be lexed; a grammar that failed reads as plain text.
    private int[]? BracketTokens(int row, TimeSpan budget)
    {
        if (Syntax is null) return [];
        if (Syntax.TokensFor(row) is { } tokens) return tokens;
        var done = Syntax.TokenizeThrough(row, budget);
        if (Syntax.TokensFor(row) is { } lexed) return lexed;
        if (done) return [];
        _bracketsLexing = true;
        return null;
    }

    private TextSpot SpotOf(Point point) => new(point.Y, CharsBefore(_lines[point.Y], point.X));

    private Point PointOf(TextSpot spot) => new(ColumnOf(_lines[spot.Row], spot.Char), spot.Row);

    // Invoke would run inline, inside this draw, which clears the request.
    private void DrawAgainSoon() => App?.AddTimeout(TimeSpan.Zero, () =>
    {
        SetNeedsDraw();
        return false;
    });
}
