using TuiCode.Syntax;

namespace TuiCode.Editor;

/// <summary>Line numbers and change markers (#23), drawn beside the text view and scrolled with it.</summary>
internal sealed class EditorGutter : View
{
    private const int MinDigits = 3;

    private static readonly Color DefaultAdded = new(0x2E, 0xA0, 0x43);
    private static readonly Color DefaultModified = new(0x38, 0x8B, 0xFD);
    private static readonly Color DefaultDeleted = new(0xF8, 0x51, 0x49);

    private readonly EditorTextView _text;
    private readonly SyntaxHighlighter? _syntax;
    private string[] _saved;
    private IReadOnlyList<string>? _committed;
    private LineChange[]? _changes;
    private IReadOnlyList<int> _pinned = [];

    public EditorGutter(EditorTextView text, SyntaxHighlighter? syntax = null)
    {
        _text = text;
        _syntax = syntax;
        _saved = [.. text.Snapshot.Refresh(text.GetAllLines())];
        Width = WidthFor(text.Lines);
        _text.ViewportChanged += (_, _) => SetNeedsDraw();
        _text.UnwrappedCursorPositionChanged += (_, _) => SetNeedsDraw();
        // Wrapped rows can shift on any edit or caret move, and the text view draws first.
        _text.DrawComplete += (_, _) =>
        {
            if (_text.SoftWrap || !_pinned.SequenceEqual(_text.Pinned)) SetNeedsDraw();
        };
    }

    /// <summary>How each buffer line differs from the baseline; recomputed lazily after an edit.</summary>
    public IReadOnlyList<LineChange> Changes => _changes ??= LineDiff.Compute(_committed ?? _saved, _text.Snapshot.Refresh(_text.GetAllLines()));

    /// <summary>The file's lines at <c>HEAD</c>, which win over the saved state; null when git has none.</summary>
    public IReadOnlyList<string>? Committed
    {
        get => _committed;
        set
        {
            _committed = value;
            OnContentChanged();
        }
    }

    public void OnContentChanged()
    {
        _changes = null;
        // Setting Width, even unchanged, clears the whole screen on the next iteration.
        Dim width = WidthFor(_text.Lines);
        if (Width != width) Width = width;
        SetNeedsDraw();
    }

    /// <summary>Take the current buffer as the saved state, which is the baseline while there's no <see cref="Committed"/>.</summary>
    public void ResetBaseline()
    {
        _saved = [.. _text.Snapshot.Refresh(_text.GetAllLines())];
        OnContentChanged();
    }

    internal static int WidthFor(int lineCount) => Math.Max(MinDigits, lineCount.ToString().Length) + 2;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var changes = Changes;
        var editable = GetAttributeForRole(VisualRole.Editable);
        var digits = Viewport.Width - 2;
        var top = _text.Viewport.Y;
        var lineNumber = ThemeColor("editorLineNumber.foreground");
        var activeLineNumber = ThemeColor("editorLineNumber.activeForeground") ?? editable.Foreground;
        var added = ThemeColor("editorGutter.addedBackground") ?? DefaultAdded;
        var modified = ThemeColor("editorGutter.modifiedBackground") ?? DefaultModified;
        var deleted = ThemeColor("editorGutter.deletedBackground") ?? DefaultDeleted;
        _pinned = [.. _text.Pinned];

        for (var row = 0; row < Viewport.Height; row++)
        {
            var (line, continuation) = row < _pinned.Count ? (_pinned[row], false) : LineOn(top + row, changes.Count);
            var background = row < _pinned.Count ? _text.PinnedAttribute(editable) : editable;
            Move(0, row);
            if (row >= _text.Viewport.Height || line >= changes.Count)
            {
                SetAttribute(editable);
                AddStr(new string(' ', Viewport.Width));
                continue;
            }

            var change = changes[line];
            var marker = change switch
            {
                LineChange.Added => added,
                LineChange.Modified => modified,
                LineChange.Deleted => deleted,
                _ => editable.Foreground,
            };
            var number = change is LineChange.Added or LineChange.Modified ? background with { Foreground = marker }
                : line == _text.CurrentRow ? background with { Foreground = activeLineNumber }
                : lineNumber is { } color ? background with { Foreground = color }
                : background with { Style = background.Style | TextStyle.Faint };
            SetAttribute(number);
            AddStr((continuation ? "" : (line + 1).ToString()).PadLeft(digits) + " ");
            SetAttribute(background with { Foreground = marker });
            AddStr(continuation && change == LineChange.Deleted ? " " : Glyph(change));
        }
        return true;
    }

    private (int Line, bool Continuation) LineOn(int screenRow, int lines)
    {
        if (!_text.SoftWrap) return (screenRow, false);
        if (screenRow >= _text.WrappedRows) return (lines, false);
        var (line, row) = _text.WrappedRowAt(screenRow);
        return (line, row > 0);
    }

    private Color? ThemeColor(string key) =>
        _syntax?.EditorColors.TryGetValue(key, out var hex) == true && Color.TryParse(hex, out Color? color) ? color : null;

    private static string Glyph(LineChange change) => change switch
    {
        LineChange.Added or LineChange.Modified => "▎",
        LineChange.Deleted => "▔",
        _ => " ",
    };
}
