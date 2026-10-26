using System.Text;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Text;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;
using TextEditor = Terminal.Gui.Editor.Editor;

namespace TuiCode.Editor;

/// <summary>
/// An editor tab's text on Terminal.Gui.Editor's document (#517). It paints syntax colours (#21) and find-in-file
/// highlights (#33), and keeps TuiCode's own carets, undo, wrapping and keys on top of Editor's single caret.
/// </summary>
internal sealed partial class CodeEditor : TextEditor
{
    // TextView's keys, which TuiCode had before it moved to Editor, rather than Editor's own defaults.
    private static readonly Dictionary<Command, PlatformKeyBinding> TextViewKeys = new()
    {
        [Command.Down] = Bind.All(Key.N.WithCtrl),
        [Command.Up] = Bind.All(Key.P.WithCtrl),
        [Command.Right] = Bind.All(Key.F.WithCtrl),
        [Command.Left] = Bind.All(Key.B.WithCtrl),
        [Command.RightEnd] = Bind.All(Key.E.WithCtrl),
        [Command.CutToEndOfLine] = Bind.All(Key.K.WithCtrl),
        [Command.CutToStartOfLine] = Bind.All(Key.Backspace.WithCtrl.WithShift),
        [Command.DeleteAll] = Bind.All(Key.Delete.WithCtrl.WithShift),
        [Command.Cut] = Bind.All(Key.W.WithCtrl),
        [Command.WordLeft] = Bind.All(Key.CursorLeft.WithCtrl),
        [Command.WordRight] = Bind.All(Key.CursorRight.WithCtrl),
        [Command.WordLeftExtend] = Bind.All(Key.CursorLeft.WithCtrl.WithShift),
        [Command.WordRightExtend] = Bind.All(Key.CursorRight.WithCtrl.WithShift),
        [Command.KillWordRight] = Bind.All(Key.Delete.WithCtrl),
        [Command.KillWordLeft] = Bind.All(Key.Backspace.WithCtrl),
        [Command.ToggleOverwrite] = Bind.All(Key.InsertChar),
    };

    private static readonly HashSet<Command> PlainMoves =
    [
        Command.Left, Command.Right, Command.Up, Command.Down, Command.LeftStart, Command.RightEnd,
        Command.WordLeft, Command.WordRight, Command.PageUp, Command.PageDown, Command.Start, Command.End,
    ];

    private static readonly Command[] KillCommands =
        [Command.CutToEndOfLine, Command.CutToStartOfLine, Command.KillWordLeft, Command.KillWordRight];

    private static readonly HashSet<Command> EditCommands =
    [
        .. KillCommands, Command.NewLine, Command.DeleteCharLeft, Command.DeleteCharRight, Command.DeleteAll,
    ];

    private readonly List<string> _lines = [""];
    private Attribute _editable;
    private Attribute _highlight;
    private Attribute _selected;
    private Attribute _cellAttribute;
    private readonly TokenPalette _palette = new();
    private long _selectionStart;
    private long _selectionEnd;

    public CodeEditor()
    {
        IndentationStrategy = null;
        Document!.UndoStack.SizeLimit = 0;
        Document.Changed += OnDocumentChanged;
        // Raised after every Changed handler, the folding manager's too, so the hidden lines are read from where the folds now are.
        Document.TextChanged += (_, _) => _foldsStale = true;
        CaretChanged += (_, _) => LeaveAutoIndents();

        AddCommand(Command.Undo, () => Undo());
        AddCommand(Command.Redo, () => Redo());
        AddCommand(Command.NextTabStop, () => IndentLines());
        AddCommand(Command.PreviousTabStop, () => OutdentLines());
        AddCommand(Command.Copy, () => CopyAtCarets(cut: false));
        AddCommand(Command.Cut, () => CopyAtCarets(cut: true));
        AddCommand(Command.Paste, () =>
        {
            PasteAtCarets(App?.Clipboard?.GetClipboardData());
            return true;
        });
        AddCommand(Command.CutToEndOfLine, CutToEndOfLine);
        AddCommand(Command.DeleteAll, () =>
        {
            SelectAll();
            return InvokeCommand(Command.DeleteCharLeft);
        });

        KeyBindings.Clear();
        ApplyKeyBindings(View.DefaultKeyBindings, TextViewKeys);
        KeyBindings.Add(Key.Enter, Command.NewLine);
        KeyBindings.Add(Key.Tab, Command.NextTabStop);
        KeyBindings.Add(Key.Tab.WithShift, Command.PreviousTabStop);
    }

    /// <summary>Row → half-open [start, end) grapheme-column ranges to highlight.</summary>
    public Dictionary<int, List<(int Start, int End)>> Highlights { get; } = new();

    /// <summary>The buffer's lines, terminators excluded. Updated in place by edits; an unchanged line keeps its string instance.</summary>
    public IReadOnlyList<string> LineStrings => _lines;

    public int Lines => _lines.Count;

    /// <summary>The length of every line in <see cref="LineStrings"/> added up, terminators excluded.</summary>
    public long LineChars { get; private set; }

    public string LineText(int row) => _lines[row];

    /// <summary>Raised once for each edit, typed, pasted, replaced, undone or set through <see cref="Text"/>.</summary>
    public event EventHandler? Edited;

    /// <summary>Raised when a key or the mouse moves the primary caret, with where it went (column X, row Y).</summary>
    public event EventHandler<Point>? CursorMoved;

    public LineTokenCache? Syntax
    {
        get;
        set
        {
            field = value;
            ForgetBrackets();
            SetNeedsDraw();
        }
    }

    /// <summary>Lexing time allowed per frame; the rest continues on later iterations.</summary>
    internal TimeSpan SyntaxBudget { get; set; } = TimeSpan.FromMilliseconds(15);

    public int TabWidth
    {
        get => IndentationSize;
        set => IndentationSize = Math.Max(value, 1);
    }

    public new string Text
    {
        get => string.Join(Environment.NewLine, _lines);
        set
        {
            _secondary.Clear();
            _autoIndents.Clear();
            ForgetFolds();
            ClearSelection();
            _editing = true;
            try
            {
                Document!.Text = value;
                CaretOffset = 0;
            }
            finally
            {
                _editing = false;
            }
            _undo.Clear();
            _undone = 0;
            _recorded.Clear();
            _recorded.AddRange(_lines);
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    public Point InsertionPoint
    {
        get => PointOf(CaretOffset);
        set => CaretOffset = OffsetOf(value);
    }

    public int CurrentRow => Document!.GetLineByOffset(CaretOffset).LineNumber - 1;

    public int CurrentColumn => InsertionPoint.X;

    public bool IsSelecting => HasSelection;

    private int AnchorOffset => !HasSelection ? CaretOffset : SelectionStart == CaretOffset ? SelectionEnd : SelectionStart;

    public Point SelectionAnchor => PointOf(AnchorOffset);

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        ForgetBrackets();
        _foldsStale = true;
        var document = Document!;
        var first = document.GetLineByOffset(Math.Min(e.Offset, document.TextLength)).LineNumber - 1;
        var removed = LineBreaks(e.RemovedText.Text) + 1;
        var inserted = LineBreaks(e.InsertedText.Text) + 1;
        if (first + removed > _lines.Count || _lines.Count - removed + inserted != document.LineCount)
        {
            _lines.Clear();
            LineChars = 0;
            for (var i = 0; i < document.LineCount; i++)
            {
                _lines.Add(Read(i));
                LineChars += _lines[i].Length;
            }
            RecordStray();
            return;
        }
        var fresh = new string[inserted];
        for (var i = 0; i < inserted; i++)
            fresh[i] = Keep(first + i < _lines.Count && i < removed ? _lines[first + i] : null, Read(first + i));
        for (var i = first; i < first + removed; i++)
            LineChars -= _lines[i].Length;
        foreach (var line in fresh)
            LineChars += line.Length;
        _lines.RemoveRange(first, removed);
        _lines.InsertRange(first, fresh);
        // A line break inserted between \r and \n moves the line above too.
        if (first > 0)
        {
            var above = Keep(_lines[first - 1], Read(first - 1));
            LineChars += above.Length - _lines[first - 1].Length;
            _lines[first - 1] = above;
        }
        RecordStray();
    }

    // Edits that don't come through a key or a TuiCode command, such as from Editor's context menu.
    private void RecordStray()
    {
        if (!_editing && RecordPending(Carets)) Edited?.Invoke(this, EventArgs.Empty);
    }

    private string Read(int row) => Document!.GetText(Document.GetLineByNumber(row + 1));

    private static string Keep(string? old, string fresh) => old == fresh ? old : fresh;

    private static int LineBreaks(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') count++;
            else if (text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n')) count++;
        }
        return count;
    }

    /// <summary>A line's graphemes, which is what a column counts.</summary>
    public string[] Graphemes(int row) => Split(_lines[row]);

    internal static string[] Split(string text)
    {
        if (IsAscii(text, text.Length))
        {
            var ascii = new string[text.Length];
            for (var i = 0; i < text.Length; i++)
                ascii[i] = AsciiStrings[text[i]];
            return ascii;
        }
        return [.. GraphemeHelper.GetGraphemes(text)];
    }

    private static readonly string[] AsciiStrings = [.. Enumerable.Range(0, 128).Select(c => ((char)c).ToString())];

    // The first n chars are graphemes of their own when they and the char after them are ASCII.
    private static bool IsAscii(string text, int n)
    {
        for (var i = 0; i < n; i++)
            if (text[i] >= 0x80) return false;
        return n >= text.Length || text[n] < 0x80;
    }

    /// <summary>The UTF-16 chars before grapheme <paramref name="column"/> of <paramref name="text"/>.</summary>
    internal static int CharsBefore(string text, int column)
    {
        column = Math.Max(column, 0);
        if (column >= text.Length && IsAscii(text, text.Length)) return text.Length;
        if (column < text.Length && IsAscii(text, column)) return column;
        var (chars, count) = (0, 0);
        foreach (var grapheme in GraphemeHelper.GetGraphemes(text))
        {
            if (count == column) break;
            chars += grapheme.Length;
            count++;
        }
        return chars;
    }

    /// <summary>The grapheme column holding UTF-16 char <paramref name="chars"/> of <paramref name="text"/>.</summary>
    internal static int ColumnOf(string text, int chars)
    {
        chars = Math.Clamp(chars, 0, text.Length);
        if (IsAscii(text, chars)) return chars;
        var (seen, count) = (0, 0);
        foreach (var grapheme in GraphemeHelper.GetGraphemes(text))
        {
            if (seen >= chars) break;
            seen += grapheme.Length;
            count++;
        }
        return count;
    }

    public int OffsetOf(Point point)
    {
        var row = Math.Clamp(point.Y, 0, _lines.Count - 1);
        return Document!.GetLineByNumber(row + 1).Offset + CharsBefore(_lines[row], point.X);
    }

    public Point PointOf(int offset)
    {
        var line = Document!.GetLineByOffset(Math.Clamp(offset, 0, Document.TextLength));
        var row = line.LineNumber - 1;
        return new Point(ColumnOf(_lines[row], offset - line.Offset), row);
    }

    public int LineLength(int row) => ColumnOf(_lines[row], _lines[row].Length);

    // So a scheme's Normal draws as an editor's text, as TextView's did.
    protected override bool OnGettingAttributeForRole(in VisualRole role, ref Attribute currentAttribute) =>
        role == VisualRole.Normal
            ? base.OnGettingAttributeForRole(VisualRole.Editable, ref currentAttribute)
            : base.OnGettingAttributeForRole(role, ref currentAttribute);

    protected override bool OnKeyDown(Key key)
    {
        SyncRowMapping();
        if (base.OnKeyDown(key)) return true;
        if (_mapped) SetNeedsDraw();
        var bound = KeyBindings.TryGet(key, out var binding);
        if (ColumnSelect && bound && ExtendColumnSelection(binding)) return true;
        // Anything else ends the box, so the next extend starts one from where the caret now is.
        _box = null;
        if (!bound) return false;
        var before = InsertionPoint;
        if (_mapped && MoveByRows(binding.Commands)) return true;
        if (HasSecondaryCarets) return InvokeAtCarets(binding);
        if (!binding.Commands.Any(EditCommands.Contains))
        {
            foreach (var command in binding.Commands)
                Move(PastLastLine(command));
            ReportMove(before);
            return true;
        }

        EditAtPrimary(() => InvokeEditCommands(binding.Commands));
        ReportMove(before);
        return true;
    }

    // Down from the last line goes to its end, as TextView's did.
    private Command PastLastLine(Command command) => CurrentRow < Lines - 1 ? command : command switch
    {
        Command.Down or Command.PageDown => Command.RightEnd,
        Command.DownExtend or Command.PageDownExtend => Command.RightEndExtend,
        _ => command,
    };

    private void ReportMove(Point before)
    {
        var after = InsertionPoint;
        if (after != before) CursorMoved?.Invoke(this, after);
    }

    // Editor's kill commands leave a selection alone, so delete the selection instead, as VS Code does.
    private void InvokeEditCommands(Command[] commands)
    {
        if (HasSelection && commands.Any(KillCommands.Contains))
        {
            InvokeCommand(Command.DeleteCharLeft);
            return;
        }
        if (commands is [Command.NewLine])
        {
            NewLineKeepingIndent();
            return;
        }
        // At the start of a line these join it to the one above, as TextView's did.
        if (commands is [Command.KillWordLeft or Command.CutToStartOfLine] && CurrentColumn == 0)
        {
            InvokeCommand(Command.DeleteCharLeft);
            return;
        }
        foreach (var command in commands)
            Move(command);
    }

    // A plain move drops the selection and moves from the caret, as TextView's did.
    private void Move(Command command)
    {
        if (HasSelection && PlainMoves.Contains(command)) ClearSelection();
        InvokeCommand(command);
    }

    // Editor's deletes nothing when the clipboard refuses the text.
    private bool? CutToEndOfLine()
    {
        if (ReadOnly) return true;
        var line = Document!.GetLineByOffset(CaretOffset);
        var end = line.Offset + line.Length;
        var (start, length) = CaretOffset < end ? (CaretOffset, end - CaretOffset) : (end, line.DelimiterLength);
        if (length == 0) return true;
        App?.Clipboard?.TrySetClipboardData(Document.GetText(start, length));
        Document.Remove(start, length);
        return true;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        Drawing?.Invoke();
        RefreshingSticky?.Invoke();
        RevealCaret();
        SyncRowMapping();
        if (_mapped) FollowCaret();
        PinForCaret();
        _editable = GetAttributeForRole(VisualRole.Editable);
        _highlight = GetAttributeForRole(VisualRole.Highlight);
        _selected = GetAttributeForRole(VisualRole.Active);
        (_selectionStart, _selectionEnd) = SelectionBounds();
        PrepareCaretSelections();
        PrepareSyntax();
        PrepareBrackets();

        var right = Viewport.Width;
        var bottom = Viewport.Height;
        var row = 0;
        if (_mapped)
        {
            DrawMappedRows(right, bottom, ref row);
        }
        else
        {
            for (var idxRow = Viewport.Y; idxRow < Lines && row < bottom; idxRow++, row++)
            {
                var line = Graphemes(idxRow);
                DrawRow(line, idxRow, row, right, FirstVisibleGlyph(line), line.Length);
            }
        }
        DrawPinned(right);
        DrawCarets();
        PlaceCursor();

        if (row < bottom)
        {
            SetAttribute(_editable);
            ClearRegion(0, row, right, bottom);
        }
        return true;
    }

    private void PrepareSyntax()
    {
        if (Syntax is null) return;
        _palette.Sync(Syntax.Highlighter);

        Syntax.Update(_lines);
        var lastVisible = _mapped ? _wrap.At(Viewport.Y + Viewport.Height - 1).Line : Math.Min(Viewport.Y + Viewport.Height, Lines) - 1;
        if (!Syntax.TokenizeThrough(lastVisible, SyntaxBudget))
            App?.Invoke(SetNeedsDraw);
    }

    // Tab stops count from first.ColWidths.
    private int DrawRow(string[] line, int idxRow, int row, int right, (int ColWidths, int Col, int Index) first, int end)
    {
        var (colWidths, col, idxCol) = first;
        var wasPreviousWideGlyphNegativeCol = false;
        Move(0, row);

        var tokens = Syntax?.TokensFor(idxRow);
        var chars = tokens is null ? 0 : CharsBefore(line, idxCol);
        var token = 0;
        var attributeToken = -1;
        _cellAttribute = _editable;

        for (; idxCol < end; idxCol++)
        {
            var text = line[idxCol];
            var cols = text.GetColumns(false);

            if (tokens is { Length: > 0 })
            {
                while (token + 2 < tokens.Length && tokens[token + 2] <= chars)
                    token += 2;
                if (token != attributeToken)
                {
                    _cellAttribute = _palette.Apply(_editable, tokens[token + 1]);
                    attributeToken = token;
                }
                chars += text.Length;
            }

            if (_drawingPinned)
                SetAttribute(_cellAttribute);
            else if (InSelection(idxCol, idxRow))
                SetAttribute(_selected);
            else if (IsHighlighted(idxCol, idxRow))
                SetAttribute(_highlight);
            else
                SetAttribute(BracketAttribute(idxCol, idxRow) ?? _cellAttribute);

            if (text == "\t")
            {
                cols = TabWidth - colWidths % TabWidth;
                if (col + cols > right) cols = right - col;
                for (var i = 0; i < cols; i++)
                    AddRune(col + i, row, (Rune)' ');
            }
            else
            {
                if (col < 0 && cols > 1)
                    wasPreviousWideGlyphNegativeCol = true;
                else
                    AddStr(col, row, text);
                cols = Math.Max(cols, 1);
            }

            if (col + cols > right) break;
            col += cols;
            colWidths += cols;

            if (idxCol + 1 < end && col + line[idxCol + 1].GetColumns() > right) break;
        }

        if (wasPreviousWideGlyphNegativeCol) AddStr(0, row, " ");

        if (col < right)
        {
            SetAttribute(_editable);
            ClearRegion(col, row, right, row + 1);
        }
        return col;
    }

    private void DrawFoldSummary(int line, int row, int col, int right)
    {
        if (col >= right || FoldSummary(line) is not { } summary) return;
        SetAttribute(_editable with { Style = _editable.Style | TextStyle.Faint });
        if (!SoftWrap) col = ColumnsBefore(Graphemes(line), LineLength(line)) - Viewport.X;
        foreach (var grapheme in Split(summary))
        {
            if (col >= right) break;
            if (col >= 0) AddStr(col, row, grapheme);
            col += Math.Max(grapheme.GetColumns(), 1);
        }
    }

    // Col is where that glyph starts relative to the viewport, so <= 0 when it straddles the left edge.
    private (int ColWidths, int Col, int Index) FirstVisibleGlyph(string[] line)
    {
        var start = Viewport.X;
        if (start <= 0 || line.Length == 0) return (0, 0, 0);

        var sum = 0;
        var count = Math.Min(start, line.Length);
        for (var i = 0; i < count; i++)
        {
            var width = Cells(line[i], sum);
            if (sum + width > start) return (sum, sum - start, i);
            sum += width;
        }
        return (sum, sum - start, count);
    }

    private int Cells(string grapheme, int column) =>
        grapheme == "\t" ? TabWidth - column % TabWidth : Math.Max(grapheme.GetColumns(), 1);

    private (long Start, long End) SelectionBounds()
    {
        if (!HasSelection) return (0, 0);
        var anchor = Encode(SelectionAnchor);
        var point = Encode(InsertionPoint);
        return anchor > point ? (point, anchor) : (anchor, point);
    }

    private bool InSelection(int col, int row)
    {
        var q = Encode(row, col);
        if (q >= _selectionStart && q < _selectionEnd) return true;
        foreach (var (start, end) in _caretSelections)
            if (q >= start && q < end) return true;
        return false;
    }

    private static long Encode(Point point) => Encode(point.Y, point.X);

    private static long Encode(int row, int col) => ((long)(uint)row << 32) | (uint)col;

    private void ClearRegion(int left, int top, int right, int bottom)
    {
        for (var row = top; row < bottom; row++)
        {
            Move(left, row);
            for (var col = left; col < right; col++)
                AddRune(col, row, (Rune)' ');
        }
    }

    private static int CharsBefore(string[] line, int idxCol)
    {
        var chars = 0;
        for (var i = 0; i < idxCol; i++)
            chars += line[i].Length;
        return chars;
    }

    private bool IsHighlighted(int idxCol, int idxRow)
    {
        if (!Highlights.TryGetValue(idxRow, out var ranges)) return false;
        foreach (var (start, end) in ranges)
            if (idxCol >= start && idxCol < end) return true;
        return false;
    }

    private int ColumnsBefore(string[] line, int column)
    {
        var columns = 0;
        for (var i = 0; i < column && i < line.Length; i++)
            columns += Cells(line[i], columns);
        return columns;
    }

    /// <summary>The primary caret's screen cell, or null while it's out of view.</summary>
    public Point? CaretOnScreen => ViewportPosition(new Caret(InsertionPoint)) is { } point ? ViewportToScreen(point) : null;

    // Editor places the cursor only in its own draw, which this replaces.
    private void PlaceCursor()
    {
        if (!CanFocus || !Enabled || ReadOnly || ViewportPosition(new Caret(InsertionPoint)) is not { } point)
        {
            Cursor = Cursor with { Position = null };
            return;
        }
        Cursor = Cursor with { Position = ViewportToScreen(point) };
        HideCursorUnderPinned();
    }
}
