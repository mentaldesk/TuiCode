namespace TuiCode.Editor;

/// <summary>Line numbers and unsaved-change markers (#23), drawn beside the text view and scrolled with it.</summary>
internal sealed class EditorGutter : View
{
    private const int MinDigits = 3;

    private static readonly Color AddedColor = new(0x2E, 0xA0, 0x43);
    private static readonly Color ModifiedColor = new(0x38, 0x8B, 0xFD);
    private static readonly Color DeletedColor = new(0xF8, 0x51, 0x49);

    private readonly EditorTextView _text;
    private IReadOnlyList<string> _baseline;
    private LineChange[]? _changes;

    public EditorGutter(EditorTextView text)
    {
        _text = text;
        _baseline = text.LineStrings;
        Width = WidthFor(text.Lines);
        _text.ViewportChanged += (_, _) => SetNeedsDraw();
        _text.UnwrappedCursorPositionChanged += (_, _) => SetNeedsDraw();
    }

    /// <summary>How each buffer line differs from the baseline; recomputed lazily after an edit.</summary>
    public IReadOnlyList<LineChange> Changes => _changes ??= LineDiff.Compute(_baseline, _text.LineStrings);

    public void OnContentChanged()
    {
        _changes = null;
        // Setting Width, even unchanged, clears the whole screen on the next iteration.
        Dim width = WidthFor(_text.Lines);
        if (Width != width) Width = width;
        SetNeedsDraw();
    }

    /// <summary>Take the current buffer as the saved state, clearing every change marker.</summary>
    public void ResetBaseline()
    {
        _baseline = _text.LineStrings;
        OnContentChanged();
    }

    internal static int WidthFor(int lineCount) => Math.Max(MinDigits, lineCount.ToString().Length) + 2;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var changes = Changes;
        var editable = GetAttributeForRole(VisualRole.Editable);
        var digits = Viewport.Width - 2;
        var top = _text.Viewport.Y;

        for (var row = 0; row < Viewport.Height; row++)
        {
            var line = top + row;
            Move(0, row);
            if (row >= _text.Viewport.Height || line >= changes.Count)
            {
                SetAttribute(editable);
                AddStr(new string(' ', Viewport.Width));
                continue;
            }

            var change = changes[line];
            var color = ColorFor(change);
            var number = editable with
            {
                Foreground = change is LineChange.Added or LineChange.Modified ? color : editable.Foreground,
                Style = line == _text.CurrentRow ? editable.Style : editable.Style | TextStyle.Faint,
            };
            SetAttribute(number);
            AddStr((line + 1).ToString().PadLeft(digits) + " ");
            SetAttribute(editable with { Foreground = color });
            AddStr(Glyph(change));
        }
        return true;
    }

    private static Color ColorFor(LineChange change) => change switch
    {
        LineChange.Added => AddedColor,
        LineChange.Modified => ModifiedColor,
        LineChange.Deleted => DeletedColor,
        _ => default,
    };

    private static string Glyph(LineChange change) => change switch
    {
        LineChange.Added or LineChange.Modified => "▎",
        LineChange.Deleted => "▔",
        _ => " ",
    };
}
