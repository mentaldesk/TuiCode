using Terminal.Gui.Drivers;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

/// <summary>An insertion point (column X, row Y), with the other end of its selection if it has one.</summary>
internal readonly record struct Caret(Point Position, Point? Anchor = null, int ColumnTrack = -1)
{
    public Point Start => Anchor is { } anchor && Before(anchor, Position) ? anchor : Position;
    public Point End => Anchor is { } anchor && Before(Position, anchor) ? anchor : Position;

    public static bool Before(Point a, Point b) => a.Y < b.Y || (a.Y == b.Y && a.X < b.X);
}

// Multiple cursors (#106): the primary caret is Editor's own; see "Multiple cursors and undo" in AGENTS.md.
internal sealed partial class CodeEditor
{
    // Run at every caret. Commands in neither set collapse to the primary caret first.
    private static readonly HashSet<Command> CaretCommands =
    [
        Command.Up, Command.UpExtend, Command.Down, Command.DownExtend,
        Command.Left, Command.LeftExtend, Command.Right, Command.RightExtend,
        Command.LeftStart, Command.LeftStartExtend, Command.RightEnd, Command.RightEndExtend,
        Command.WordLeft, Command.WordLeftExtend, Command.WordRight, Command.WordRightExtend,
        Command.PageUp, Command.PageUpExtend, Command.PageDown, Command.PageDownExtend,
        Command.NewLine, Command.DeleteCharLeft, Command.DeleteCharRight, Command.KillWordLeft, Command.KillWordRight,
        Command.CutToEndOfLine, Command.CutToStartOfLine,
    ];

    private static readonly HashSet<Command> CaretIndependentCommands =
        [Command.Undo, Command.Redo, Command.ToggleOverwrite, Command.Copy, Command.Cut];

    private readonly List<Caret> _secondary = [];
    private readonly List<(long Start, long End)> _caretSelections = [];
    private int _columnTrack = -1;
    private bool _visitingCarets;
    private string? _copiedText;
    private string[] _copiedPieces = [];
    private bool _copiedLine;

    /// <summary>True while edits move Editor's caret from caret to caret.</summary>
    internal bool IsVisitingCarets => _visitingCarets;

    public bool HasSecondaryCarets => _secondary.Count > 0;

    public int CaretCount => _secondary.Count + 1;

    /// <summary>Every caret, primary first.</summary>
    public Caret[] Carets => [PrimaryCaret, .. _secondary];

    private Caret PrimaryCaret => new(InsertionPoint, HasSelection ? SelectionAnchor : null, _columnTrack);

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="position"/>, scrolling the caret into view.</summary>
    internal void Select(Point anchor, Point position)
    {
        Load(new Caret(position, anchor), reveal: true);
        SetNeedsDraw();
    }

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
        // Before the primary moves, so anything watching the move sees every caret where it will be.
        _secondary.Clear();
        _secondary.AddRange(merged.Skip(1));
        Load(merged[0], reveal);
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
            added.Add(new Caret(new Point(Math.Min(track, LineLength(row)), row), ColumnTrack: track));
        }
        SetCarets(added, reveal: false);

        if (SoftWrap)
        {
            RefreshWrap();
            var edgeCaret = step < 0 ? added.MinBy(c => (c.Position.Y, c.Position.X)) : added.MaxBy(c => (c.Position.Y, c.Position.X));
            ScrollRowIntoView(Locate(edgeCaret.Position).Row);
            return;
        }
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

        var step = up ? -1 : 1;
        Edit(() =>
        {
            foreach (var (first, last) in up ? blocks : Enumerable.Reverse(blocks))
            {
                var top = up ? first - 1 : first;
                string[] rows = [.. Enumerable.Range(top, last - first + 2).Select(LineText)];
                string[] moved = up ? [.. rows[1..], rows[0]] : [rows[^1], .. rows[..^1]];
                ReplaceLineRange(top, rows.Length, moved);
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
        Edit(() =>
        {
            for (var b = blocks.Count - 1; b >= 0; b--)
            {
                var (first, last) = blocks[b];
                ReplaceLineRange(last + 1, 0, [.. Enumerable.Range(first, last - first + 1).Select(LineText)]);
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

    /// <summary>Replaces <paramref name="count"/> lines from <paramref name="start"/> with <paramref name="lines"/>, as one undo step.</summary>
    public void ReplaceLines(int start, int count, IReadOnlyList<string> lines)
    {
        if (ReadOnly) return;
        var carets = Carets;
        Edit(() =>
        {
            ReplaceLineRange(start, count, [.. lines]);
            return carets;
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
        if (commands.All(CaretIndependentCommands.Contains))
        {
            foreach (var command in commands)
                InvokeCommand(command);
            return true;
        }
        switch (commands)
        {
            case [Command.Paste]:
                PasteAtCarets(App?.Clipboard?.GetClipboardData());
                return true;
            case [Command.NextTabStop]:
                return IndentLines();
            case [Command.PreviousTabStop]:
                return OutdentLines();
        }
        if (!commands.All(CaretCommands.Contains))
        {
            RemoveSecondaryCarets();
            foreach (var command in commands)
                InvokeCommand(command);
            return true;
        }
        if (VerticalRows(commands) is var (rows, extend))
            AtEachCaret(_ => MoveVertically(rows, extend));
        else
            AtEachCaret(_ => InvokeEditCommands(commands));
        return true;
    }

    private (int Rows, bool Extend)? VerticalRows(Command[] commands) => commands switch
    {
        [Command.Up] => (-1, false),
        [Command.UpExtend] => (-1, true),
        [Command.Down] => (1, false),
        [Command.DownExtend] => (1, true),
        [Command.PageUp] => (-Math.Max(Viewport.Height, 1), false),
        [Command.PageUpExtend] => (-Math.Max(Viewport.Height, 1), true),
        [Command.PageDown] => (Math.Max(Viewport.Height, 1), false),
        [Command.PageDownExtend] => (Math.Max(Viewport.Height, 1), true),
        _ => null,
    };

    // Editor's own vertical move keeps one sticky column, so each caret keeps its own here.
    private void MoveVertically(int rows, bool extend)
    {
        var from = PrimaryCaret;
        var track = from.ColumnTrack >= 0 ? from.ColumnTrack : from.Position.X;
        var row = Math.Clamp(from.Position.Y + rows, 0, Lines - 1);
        var to = from.Position.Y + rows > Lines - 1 ? new Point(LineLength(row), row) : new Point(Math.Min(track, LineLength(row)), row);
        Load(new Caret(to, extend ? from.Anchor ?? from.Position : null, track), reveal: false);
    }

    protected override bool OnKeyDownNotHandled(Key key)
    {
        // Editor's own column select and extra carets live here; TuiCode has its own.
        if (key.IsCtrl || (key.IsAlt && string.IsNullOrEmpty(key.AssociatedText))) return false;
        // An Alt key with text is the Option character a macOS terminal composed, e.g. Option+P's π (#553).
        if (key.IsAlt) key = key.NoAlt;
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
    private Point ToEnd(Point point) => new(LineLength(point.Y) - point.X, Lines - 1 - point.Y);

    private Point FromEnd(Point fromEnd)
    {
        var row = Math.Clamp(Lines - 1 - fromEnd.Y, 0, Lines - 1);
        return new Point(Math.Max(LineLength(row) - fromEnd.X, 0), row);
    }

    /// <summary>Raised after every copy and cut, saying whether the text reached the clipboard.</summary>
    public event EventHandler<CopyOutcome>? Copied;

    internal ClipboardTools? ClipboardFallback { get; set; } = ClipboardTools.ThisMachine;

    /// <summary>Copies <paramref name="text"/> as if it had been selected, so a paste puts it at the caret.</summary>
    public CopyOutcome CopyText(string text)
    {
        var outcome = VerifiedClipboard.Write(App?.Clipboard, text, ClipboardFallback);
        if (outcome is CopyOutcome.Failed) return outcome;
        _copiedPieces = [text];
        _copiedText = text;
        _copiedLine = false;
        return outcome;
    }

    private bool CopyAtCarets(bool cut)
    {
        var ordered = Carets.OrderBy(c => c.Start.Y).ThenBy(c => c.Start.X).ToArray();
        string[] pieces = [.. ordered.Select(c => c.Anchor is null ? LineText(c.Position.Y) : TextBetween(c.Start, c.End))];
        var text = string.Join(Environment.NewLine, pieces);
        var outcome = VerifiedClipboard.Write(App?.Clipboard, text, ClipboardFallback);
        Copied?.Invoke(this, outcome);
        if (outcome is CopyOutcome.Failed) return true;

        _copiedPieces = pieces;
        _copiedText = text;
        // So a paste at one caret puts a whole copied line above the caret's, as TextView's did.
        _copiedLine = ordered is [{ Anchor: null }];
        if (!cut || ReadOnly) return true;

        AtEachCaret(_ =>
        {
            if (!HasSelection) SelectCurrentLine();
            InvokeCommand(Command.DeleteCharLeft);
        });
        return true;
    }

    private IApplication? _pasteSource;

    // Editor reads a bracketed paste from the clipboard rather than the terminal, so it's taken before Editor sees it.
    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView)
    {
        base.OnHasFocusChanged(newHasFocus, previousFocusedView, focusedView);
        if (!newHasFocus || App is not { } app || app == _pasteSource) return;
        if (_pasteSource is not null) _pasteSource.Paste -= OnTerminalPaste;
        _pasteSource = app;
        app.Paste += OnTerminalPaste;
    }

    private void OnTerminalPaste(object? sender, PasteEventArgs e)
    {
        if (e.Handled || App?.Navigation?.GetFocused() != this) return;
        PasteAtCarets(e.Text.ReplaceLineEndings("\n"));
        e.Handled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _pasteSource is not null) _pasteSource.Paste -= OnTerminalPaste;
        base.Dispose(disposing);
    }

    private void PasteAtCarets(string? text)
    {
        if (ReadOnly || text is not { Length: > 0 }) return;
        var carets = Carets;
        if (carets is [{ Anchor: null } only] && _copiedLine && text == _copiedText)
        {
            Edit(() =>
            {
                Document!.Insert(OffsetOf(only.Position with { X = 0 }), text + "\n");
                return [Shift(only, LineBreaks(text) + 1)];
            });
            return;
        }
        var pieces = text == _copiedText && _copiedPieces.Length == carets.Length ? _copiedPieces : Split(text);
        if (pieces.Length != carets.Length) pieces = [.. carets.Select(_ => text)];
        var ranks = Enumerable.Range(0, carets.Length).OrderBy(i => carets[i].Start.Y).ThenBy(i => carets[i].Start.X).ToArray();

        AtEachCaret(i =>
        {
            if (HasSelection) InvokeCommand(Command.DeleteCharLeft);
            Document!.Insert(CaretOffset, pieces[Array.IndexOf(ranks, i)]);
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
                ? (new Point(LineLength(row - 1), row - 1), new Point(LineLength(row), row))
                : (new Point(0, row), new Point(LineLength(row), row));
        Load(new Caret(end, anchor), reveal: false);
    }

    private string TextBetween(Point start, Point end)
    {
        var first = Graphemes(start.Y);
        if (start.Y == end.Y) return string.Concat(first[start.X..Math.Min(end.X, first.Length)]);
        return string.Join(Environment.NewLine,
            [string.Concat(first[Math.Min(start.X, first.Length)..]), .. Enumerable.Range(start.Y + 1, end.Y - start.Y - 1).Select(LineText), string.Concat(Graphemes(end.Y)[..end.X])]);
    }

    // Past the last line is the end of the buffer.
    private Point Clamp(Point point)
    {
        if (point.Y >= Lines) return new Point(LineLength(Lines - 1), Lines - 1);
        var row = Math.Max(point.Y, 0);
        return new Point(Math.Clamp(point.X, 0, LineLength(row)), row);
    }

    // Editor's public members can only select forwards, but its caret keeps the selection's anchor when set.
    private void Load(Caret caret, bool reveal)
    {
        var viewport = Viewport;
        var position = OffsetOf(Clamp(caret.Position));
        var anchor = caret.Anchor is { } a ? OffsetOf(Clamp(a)) : position;
        ClearSelection();
        if (anchor < position)
        {
            SelectRange(anchor, position - anchor);
        }
        else if (anchor > position)
        {
            CaretOffset = anchor;
            InvokeCommand(Command.LeftExtend);
            CaretOffset = position;
        }
        else
        {
            CaretOffset = position;
        }
        _columnTrack = caret.ColumnTrack;
        if (!reveal) Viewport = viewport;
        if (SoftWrap) SetNeedsDraw();
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

    private void ToggleCaret(Caret[] carets, Point clicked)
    {
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
                _caretSelections.Add((Encode(caret.Start), Encode(caret.End)));
    }

    internal IEnumerable<Point> SecondaryCaretsOnScreen() =>
        _secondary.Select(ViewportPosition).OfType<Point>().Select(point => ViewportToScreen(point));

    // Where the terminal can't draw the extra carets, every caret, the primary included, is painted as a block over the character it's before.
    private void DrawCarets()
    {
        var paint = HasSecondaryCarets && !(HasFocus && TerminalCursors.IsSupportedBy(App));
        var style = paint ? CursorStyle.Hidden : OverwriteMode ? CursorStyle.SteadyBlock : TextField.DefaultCursorStyle;
        if (Cursor.Style != style) Cursor = Cursor with { Style = style };
        if (!paint) return;

        var block = new Attribute(_editable.Background, _editable.Foreground);
        foreach (var caret in Carets)
        {
            if (ViewportPosition(caret) is not { } point) continue;
            var line = Graphemes(caret.Position.Y);
            var grapheme = caret.Position.X < line.Length ? line[caret.Position.X] : " ";
            SetAttribute(block);
            AddStr(point.X, point.Y, grapheme == "\t" ? " " : grapheme);
        }
    }

    private Point? ViewportPosition(Caret caret)
    {
        if (SoftWrap)
        {
            var (screenRow, column) = Locate(caret.Position);
            var y = screenRow - Viewport.Y;
            return y < _pinned.Count || y >= Viewport.Height || column >= Viewport.Width ? null : new Point(column, y);
        }
        var row = caret.Position.Y - Viewport.Y;
        if (row < _pinned.Count || row >= Viewport.Height || caret.Position.Y >= Lines) return null;
        var x = ColumnsBefore(Graphemes(caret.Position.Y), caret.Position.X) - Viewport.X;
        return x < 0 || x >= Viewport.Width ? null : new Point(x, row);
    }
}
