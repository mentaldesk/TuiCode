using System.Runtime.CompilerServices;
using Terminal.Gui.Drivers;
using Terminal.Gui.Text;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

/// <summary>An insertion point (column X, row Y), with the other end of its selection if it has one.</summary>
internal readonly record struct Caret(Point Position, Point? Anchor = null, int ColumnTrack = -1, bool Extending = false)
{
    public Point Start => Anchor is { } anchor && Before(anchor, Position) ? anchor : Position;
    public Point End => Anchor is { } anchor && Before(Position, anchor) ? anchor : Position;

    public static bool Before(Point a, Point b) => a.Y < b.Y || (a.Y == b.Y && a.X < b.X);
}

// Multiple cursors (#106): the primary caret is TG's own; see "Multiple cursors and undo" in AGENTS.md.
internal sealed partial class EditorTextView
{
    // Run at every caret. Commands in neither set collapse to the primary caret first.
    private static readonly HashSet<Command> CaretCommands =
    [
        Command.Up, Command.UpExtend, Command.Down, Command.DownExtend,
        Command.Left, Command.LeftExtend, Command.Right, Command.RightExtend,
        Command.LeftStart, Command.LeftStartExtend, Command.RightEnd, Command.RightEndExtend,
        Command.WordLeft, Command.WordLeftExtend, Command.WordRight, Command.WordRightExtend,
        Command.PageUp, Command.PageUpExtend, Command.PageDown, Command.PageDownExtend, Command.ToggleExtend,
        Command.NewLine, Command.DeleteCharLeft, Command.DeleteCharRight, Command.KillWordLeft, Command.KillWordRight,
        Command.CutToEndOfLine, Command.CutToStartOfLine, Command.NextTabStop, Command.PreviousTabStop,
    ];

    private static readonly HashSet<Command> CaretIndependentCommands =
        [Command.Undo, Command.Redo, Command.ToggleOverwrite, Command.EnableOverwrite, Command.DisableOverwrite];

    private readonly List<Caret> _secondary = [];
    private readonly List<(long Start, long End)> _caretSelections = [];
    private bool _visitingCarets;
    private string? _copiedText;
    private string[] _copiedPieces = [];

    /// <summary>True while edits move TG's insertion point from caret to caret.</summary>
    internal bool IsVisitingCarets => _visitingCarets;

    public bool HasSecondaryCarets => _secondary.Count > 0;

    public int CaretCount => _secondary.Count + 1;

    /// <summary>Every caret, primary first.</summary>
    public Caret[] Carets => [PrimaryCaret, .. _secondary];

    private Caret PrimaryCaret => new(InsertionPoint,
        IsSelecting ? new Point(SelectionStartColumn, SelectionStartRow) : null, ColumnTrack(this), IsSelecting && ShiftSelecting(this));

    public void RemoveSecondaryCarets()
    {
        // Going back to one caret ends any column-select box too (#114).
        _box = null;
        if (_secondary.Count == 0) return;
        _secondary.Clear();
        SetNeedsDraw();
    }

    /// <summary>Makes <paramref name="carets"/> (primary first) the carets, merging any that overlap.</summary>
    public void SetCarets(IReadOnlyList<Caret> carets, bool reveal = true)
    {
        var merged = Merge([.. carets.Select(c => c with { Position = Clamp(c.Position), Anchor = c.Anchor is { } a ? Clamp(a) : null })]);
        Load(merged[0], reveal);
        _secondary.Clear();
        _secondary.AddRange(merged.Skip(1));
        SetNeedsDraw();
    }

    /// <summary>Adds a caret on the line above or below every caret, at the same column or the end of a shorter line.</summary>
    public void AddCaret(LineDirection direction)
    {
        var step = direction == LineDirection.Up ? -1 : 1;
        var carets = Carets;
        List<Caret> added = [.. carets];
        foreach (var caret in carets)
        {
            var row = caret.Position.Y + step;
            if (row < 0 || row >= Lines) continue;
            var track = caret.ColumnTrack >= 0 ? caret.ColumnTrack : caret.Position.X;
            added.Add(new Caret(new Point(Math.Min(track, GetLine(row).Count), row), ColumnTrack: track));
        }
        SetCarets(added, reveal: false);

        var edge = step < 0 ? added.Min(c => c.Position.Y) : added.Max(c => c.Position.Y);
        if (edge < Viewport.Y)
            Viewport = Viewport with { Y = edge };
        else if (edge >= Viewport.Y + Viewport.Height)
            Viewport = Viewport with { Y = edge - Viewport.Height + 1 };
    }

    /// <summary>Swap the lines under each caret with the line beyond them, as one undo step.</summary>
    public void MoveLines(LineDirection direction)
    {
        var carets = Carets;
        var blocks = RowBlocks(carets);
        var up = direction == LineDirection.Up;
        if (ReadOnly || (up ? blocks[0].First == 0 : blocks[^1].Last == Lines - 1)) return;

        var model = ModelField.GetValue(this)!;
        var step = up ? -1 : 1;
        Edit(() =>
        {
            foreach (var (first, last) in up ? blocks : Enumerable.Reverse(blocks))
            {
                var top = up ? first - 1 : first;
                List<List<Cell>> rows = [.. Enumerable.Range(top, last - first + 2).Select(row => new List<Cell>(GetLine(row)))];
                List<List<Cell>> moved = up ? [.. rows[1..], rows[0]] : [rows[^1], .. rows[..^1]];
                for (var i = 0; i < moved.Count; i++)
                    ReplaceLine(model, top + i, moved[i]);
            }
            return [.. carets.Select(caret => Shift(caret, step))];
        });
    }

    /// <summary>Copy the lines under each caret above or below themselves, as one undo step.</summary>
    public void DuplicateLines(LineDirection direction)
    {
        if (ReadOnly) return;
        var carets = Carets;
        var blocks = RowBlocks(carets);
        var model = ModelField.GetValue(this)!;
        Edit(() =>
        {
            for (var b = blocks.Count - 1; b >= 0; b--)
            {
                var (first, last) = blocks[b];
                for (var row = first; row <= last; row++)
                    AddLine(model, last + 1 + row - first, new List<Cell>(GetLine(row)));
            }
            return [.. carets.Select(caret =>
            {
                var rows = 0;
                foreach (var (first, last) in blocks)
                {
                    if (first > caret.Start.Y) break;
                    if (last >= caret.Start.Y && direction == LineDirection.Up) break;
                    rows += last - first + 1;
                }
                return Shift(caret, rows);
            })];
        });
    }

    private static Caret Shift(Caret caret, int rows) => caret with
    {
        Position = caret.Position with { Y = caret.Position.Y + rows },
        Anchor = caret.Anchor is { } anchor ? anchor with { Y = anchor.Y + rows } : null,
    };

    // The rows each caret covers, merged into contiguous blocks. A selection ending at the start of a line doesn't cover it.
    private static List<(int First, int Last)> RowBlocks(IEnumerable<Caret> carets)
    {
        var blocks = new List<(int First, int Last)>();
        foreach (var (first, last) in carets
                     .Select(c => (c.Start.Y, c.End.X == 0 && c.End.Y > c.Start.Y ? c.End.Y - 1 : c.End.Y))
                     .OrderBy(block => block.Item1))
        {
            if (blocks.Count > 0 && first <= blocks[^1].Last + 1)
                blocks[^1] = (blocks[^1].First, Math.Max(blocks[^1].Last, last));
            else
                blocks.Add((first, last));
        }
        return blocks;
    }

    private bool InvokeAtCarets(KeyBinding binding)
    {
        var commands = binding.Commands;
        if (commands.All(CaretIndependentCommands.Contains)) return false;
        switch (commands)
        {
            case [Command.Copy]:
                CopyAtCarets(cut: false);
                return true;
            case [Command.Cut]:
                CopyAtCarets(cut: true);
                return true;
            case [Command.Paste]:
                PasteAtCarets();
                return true;
        }
        if (!commands.All(CaretCommands.Contains))
        {
            RemoveSecondaryCarets();
            return false;
        }
        AtEachCaret(_ => InvokeEditCommands(commands, binding));
        return true;
    }

    protected override bool OnKeyDownNotHandled(Key key)
    {
        var handled = false;
        if (_secondary.Count == 0)
            EditAtPrimary(() => handled = base.OnKeyDownNotHandled(key));
        else
            AtEachCaret(_ => handled |= base.OnKeyDownNotHandled(key));
        return handled;
    }

    // Visits the carets last to first, so an edit only moves carets already visited. Those are held relative to the end of the buffer, which the edit doesn't change.
    private void AtEachCaret(Action<int> action) =>
        Edit(() =>
        {
            var carets = Carets;
            var visited = new Caret[carets.Length];
            foreach (var i in Enumerable.Range(0, carets.Length).OrderByDescending(i => carets[i].Start.Y).ThenByDescending(i => carets[i].Start.X))
            {
                Load(carets[i], reveal: false);
                action(i);
                var caret = PrimaryCaret;
                visited[i] = caret with { Position = ToEnd(caret.Position), Anchor = caret.Anchor is { } a ? ToEnd(a) : null };
            }
            return [.. visited.Select(c => c with { Position = FromEnd(c.Position), Anchor = c.Anchor is { } a ? FromEnd(a) : null })];
        });

    // (columns to the end of the line, lines to the end of the buffer)
    private Point ToEnd(Point point) => new(GetLine(point.Y).Count - point.X, Lines - 1 - point.Y);

    private Point FromEnd(Point fromEnd)
    {
        var row = Math.Clamp(Lines - 1 - fromEnd.Y, 0, Lines - 1);
        return new Point(Math.Max(GetLine(row).Count - fromEnd.X, 0), row);
    }

    private void CopyAtCarets(bool cut)
    {
        var ordered = Carets.OrderBy(c => c.Start.Y).ThenBy(c => c.Start.X).ToArray();
        _copiedPieces = [.. ordered.Select(c => c.Anchor is null ? Cell.ToString(GetLine(c.Position.Y)) : TextBetween(c.Start, c.End))];
        _copiedText = string.Join(Environment.NewLine, _copiedPieces);
        App?.Clipboard?.SetClipboardData(_copiedText);
        if (!cut || ReadOnly) return;

        AtEachCaret(_ =>
        {
            if (!IsSelecting) SelectCurrentLine();
            DeleteCharLeft();
        });
    }

    private void PasteAtCarets()
    {
        if (ReadOnly || App?.Clipboard?.GetClipboardData() is not { Length: > 0 } text) return;
        var carets = Carets;
        var pieces = text == _copiedText && _copiedPieces.Length == carets.Length ? _copiedPieces : Split(text);
        if (pieces.Length != carets.Length) pieces = [.. carets.Select(_ => text)];
        var ranks = Enumerable.Range(0, carets.Length).OrderBy(i => carets[i].Start.Y).ThenBy(i => carets[i].Start.X).ToArray();

        AtEachCaret(i =>
        {
            if (IsSelecting) DeleteCharLeft();
            IsSelecting = false;
            CopyWithoutSelection(this) = false;
            InsertAllText(this, pieces[Array.IndexOf(ranks, i)], false);
        });

        static string[] Split(string text)
        {
            text = text.ReplaceLineEndings("\n");
            return (text.EndsWith('\n') ? text[..^1] : text).Split('\n');
        }
    }

    // Including its line break, so deleting it removes the line.
    private void SelectCurrentLine()
    {
        var row = CurrentRow;
        var (anchor, end) = row + 1 < Lines
            ? (new Point(0, row), new Point(0, row + 1))
            : row > 0
                ? (new Point(GetLine(row - 1).Count, row - 1), new Point(GetLine(row).Count, row))
                : (new Point(0, row), new Point(GetLine(row).Count, row));
        Load(new Caret(end, anchor, Extending: true), reveal: false);
    }

    private string TextBetween(Point start, Point end)
    {
        if (start.Y == end.Y) return Cell.ToString(GetLine(start.Y)[start.X..end.X]);
        var first = GetLine(start.Y);
        return string.Join(Environment.NewLine,
            [Cell.ToString(first[start.X..]), .. Enumerable.Range(start.Y + 1, end.Y - start.Y - 1).Select(row => Cell.ToString(GetLine(row))), Cell.ToString(GetLine(end.Y)[..end.X])]);
    }

    // Past the last line is the end of the buffer.
    private Point Clamp(Point point)
    {
        if (point.Y >= Lines) return new Point(GetLine(Lines - 1).Count, Lines - 1);
        var row = Math.Max(point.Y, 0);
        return new Point(Math.Clamp(point.X, 0, GetLine(row).Count), row);
    }

    private void Load(Caret caret, bool reveal)
    {
        var position = Clamp(caret.Position);
        if (reveal)
        {
            InsertionPoint = position;
        }
        else
        {
            SetCurrentRow(this, position.Y);
            SetCurrentColumn(this, position.X);
        }

        if (caret.Anchor is { } anchor)
        {
            anchor = Clamp(anchor);
            SelectionStartRowField(this) = anchor.Y;
            SelectionStartColumnField(this) = anchor.X;
        }
        IsSelecting = caret.Anchor is not null;
        ShiftSelecting(this) = caret.Extending && caret.Anchor is not null;
        ColumnTrack(this) = caret.ColumnTrack;
    }

    private static List<Caret> Merge(IReadOnlyList<Caret> carets)
    {
        var order = Enumerable.Range(0, carets.Count)
            .OrderBy(i => carets[i].Start.Y).ThenBy(i => carets[i].Start.X).ThenBy(i => i).ToList();
        var merged = new List<(Caret Caret, bool Primary)>();
        foreach (var i in order)
        {
            var next = carets[i];
            if (merged.Count > 0 && Overlap(merged[^1].Caret, next))
            {
                var (last, primary) = merged[^1];
                var kept = primary ? last : next;
                var start = last.Start;
                var end = Caret.Before(last.End, next.End) ? next.End : last.End;
                var forward = kept.Anchor is null || kept.Position == kept.End;
                merged[^1] = (kept with
                {
                    Position = forward ? end : start,
                    Anchor = start == end ? null : forward ? start : end,
                }, primary || i == 0);
            }
            else
            {
                merged.Add((next, i == 0));
            }
        }
        var primaryIndex = merged.FindIndex(m => m.Primary);
        return [merged[primaryIndex].Caret, .. merged.Where((_, i) => i != primaryIndex).Select(m => m.Caret)];

        static bool Overlap(Caret a, Caret b) =>
            Caret.Before(b.Start, a.End) || b.Position == a.Position || (b.Start == a.Start && b.End == a.End);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        const MouseFlags leftButton = MouseFlags.LeftButtonPressed | MouseFlags.LeftButtonReleased | MouseFlags.LeftButtonClicked
                                      | MouseFlags.LeftButtonDoubleClicked | MouseFlags.LeftButtonTripleClicked;
        if ((mouse.Flags & leftButton) == 0) return base.OnMouseEvent(mouse);

        if (!mouse.Flags.HasFlag(MouseFlags.Alt))
        {
            RemoveSecondaryCarets();
            return base.OnMouseEvent(mouse);
        }

        if (CanFocus && !HasFocus) SetFocus();
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)) ToggleCaretAt(mouse);
        return true;
    }

    private void ToggleCaretAt(Mouse mouse)
    {
        var carets = Carets;
        ProcessMouseClick(this, mouse, out _);
        var clicked = InsertionPoint;
        var index = Array.FindIndex(carets, c => c.Position == clicked);
        if (index < 0)
            SetCarets([.. carets, new Caret(clicked)], reveal: false);
        else if (carets.Length > 1)
            SetCarets([.. carets.Where((_, i) => i != index)], reveal: false);
        else
            Load(carets[0], reveal: false);
    }

    private void PrepareCaretSelections()
    {
        _caretSelections.Clear();
        foreach (var caret in _secondary)
            if (caret.Anchor is not null)
                _caretSelections.Add((Encode(caret.Start.Y, caret.Start.X), Encode(caret.End.Y, caret.End.X)));
    }

    internal IEnumerable<Point> SecondaryCaretsOnScreen() =>
        _secondary.Select(ViewportPosition).OfType<Point>().Select(point => ViewportToScreen(point));

    internal const string Bar = "\u258f";

    // Where the terminal can't draw the extra carets, every caret, the primary included, is painted as the bar it would have drawn.
    private void DrawCarets()
    {
        var paint = HasSecondaryCarets && !(HasFocus && TerminalCursors.IsSupportedBy(App));
        var style = paint ? CursorStyle.Hidden : DefaultCursorStyle;
        if (Cursor.Style != style) Cursor = Cursor with { Style = style };
        if (!paint) return;

        foreach (var caret in Carets)
        {
            if (ViewportPosition(caret) is not { } point) continue;
            SetAttribute(_editable);
            AddStr(point.X, point.Y, Bar);
        }
    }

    private Point? ViewportPosition(Caret caret)
    {
        var row = caret.Position.Y - Viewport.Y;
        if (row < 0 || row >= Viewport.Height || caret.Position.Y >= Lines) return null;
        var line = GetLine(caret.Position.Y);
        var x = ColumnsBefore(line, Math.Min(caret.Position.X, line.Count)) - Viewport.X;
        return x < 0 || x >= Viewport.Width ? null : new Point(x, row);
    }

    private int ColumnsBefore(List<Cell> line, int column)
    {
        var columns = 0;
        for (var i = 0; i < column; i++)
        {
            var grapheme = line[i].Grapheme;
            columns += grapheme == "\t"
                ? TabWidth > 0 ? TabWidth - columns % TabWidth : 0
                : Math.Max(grapheme.GetColumns(), 1);
        }
        return columns;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_columnTrack")]
    private static extern ref int ColumnTrack(TextView view);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_shiftSelecting")]
    private static extern ref bool ShiftSelecting(TextView view);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_copyWithoutSelection")]
    private static extern ref bool CopyWithoutSelection(TextView view);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_selectionStartRow")]
    private static extern ref int SelectionStartRowField(TextView view);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_selectionStartColumn")]
    private static extern ref int SelectionStartColumnField(TextView view);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_CurrentRow")]
    private static extern void SetCurrentRow(TextView view, int row);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_CurrentColumn")]
    private static extern void SetCurrentColumn(TextView view, int column);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "InsertAllText")]
    private static extern void InsertAllText(TextView view, string text, bool fromClipboard);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ProcessMouseClick")]
    private static extern void ProcessMouseClick(TextView view, Mouse mouse, out List<Cell> line);
}
