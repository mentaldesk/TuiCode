using System.Runtime.CompilerServices;
using Terminal.Gui.Drivers;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace TuiCode.Editor;

// Soft wrap (#378); see "Word wrap" in AGENTS.md.
internal sealed partial class EditorTextView
{
    private readonly WrapMap _wrap = new();
    private (int Row, int Column)? _followed;
    private bool _barScrolling;
    private bool _followingBar;
    private Point? _dragAnchor;
    private (Point At, int X)? _rowTrack;

    public bool SoftWrap
    {
        get;
        set
        {
            if (field == value) return;
            var topLine = TopLine;
            field = value;
            RemoveSecondaryCarets();
            _followed = null;
            if (value)
            {
                FollowVerticalBar();
                RefreshWrap();
                ScrollToRow(_wrap.FirstRow(topLine));
            }
            else
            {
                base.Viewport = Viewport with { Y = topLine };
            }
            SetNeedsLayout();
            SetNeedsDraw();
        }
    }

    internal int WrappedRows => _wrap.Rows;

    internal (int Line, int Row) WrappedRowAt(int screenRow) => _wrap.At(screenRow);

    private int TopLine => SoftWrap ? _wrap.At(Viewport.Y).Line : Viewport.Y;

    // TG positions the view by file line; while wrapped the caret's screen row decides that, at the next draw.
    public override Rectangle Viewport
    {
        get => base.Viewport;
        set
        {
            if (!SoftWrap)
            {
                base.Viewport = value;
                return;
            }
            base.Viewport = _barScrolling ? value with { X = 0 } : value with { X = 0, Y = base.Viewport.Y };
            if (_barScrolling) return;
            _followed = null;
            SetNeedsDraw();
        }
    }

    protected override bool OnContentSizeChanging(ValueChangingEventArgs<Size?> args)
    {
        if (SoftWrap) args.NewValue = new Size(Viewport.Width, _wrap.Rows);
        return base.OnContentSizeChanging(args);
    }

    internal void RevealWrappedLines(int first, int last)
    {
        RefreshWrap();
        var lastRow = _wrap.FirstRow(last + 1) - 1;
        if (Reveal.TopRow(Viewport.Y, Viewport.Height, _wrap.Rows, _wrap.FirstRow(first), lastRow) is { } top)
            ScrollToRow(top);
    }

    // The scroll bar moves the view by screen row, so its writes go through unchanged.
    private void FollowVerticalBar()
    {
        if (_followingBar || !ViewportSettings.HasFlag(ViewportSettingsFlags.HasScrollBars)) return;
        _followingBar = true;
        VerticalScrollBar.ValueChanging += (_, e) => _barScrolling = !e.Handled;
        VerticalScrollBar.ValueChanged += (_, _) => _barScrolling = false;
    }

    private void ScrollToRow(int row) =>
        base.Viewport = Viewport with { X = 0, Y = Math.Clamp(row, 0, Math.Max(_wrap.Rows - Viewport.Height, 0)) };

    // One column for a scroll bar, so showing it doesn't rewrap, and one for the caret at the end of a full row.
    private int WrapWidth =>
        Math.Max(Viewport.Width + (ViewportSettings.HasFlag(ViewportSettingsFlags.HasScrollBars) ? (Padding?.Thickness.Right ?? 0) - 1 : 0) - 1, 1);

    private void RefreshWrap()
    {
        var resized = WrapWidth != _wrap.Width || TabWidth != _wrap.TabWidth;
        var top = _wrap.At(Viewport.Y);
        if (!_wrap.Update(Snapshot.Refresh(GetAllLines()), Graphemes, WrapWidth, TabWidth)) return;

        SetContentSize(new Size(Viewport.Width, _wrap.Rows));
        if (resized) ScrollToRow(_wrap.FirstRow(top.Line) + Math.Min(top.Row, _wrap.Starts(Math.Min(top.Line, Lines - 1)).Length - 1));
    }

    private IReadOnlyList<string> Graphemes(int line) => GetLine(line).ConvertAll(cell => cell.Grapheme);

    private void FollowCaret()
    {
        RefreshWrap();
        var caret = (CurrentRow, CurrentColumn);
        if (_followed == caret) return;
        _followed = caret;
        var row = Locate(InsertionPoint).Row;
        if (row < Viewport.Y) ScrollToRow(row);
        else if (row >= Viewport.Y + Viewport.Height) ScrollToRow(row - Viewport.Height + 1);
    }

    private (int Row, int X) Locate(Point position)
    {
        var line = Math.Clamp(position.Y, 0, _wrap.Lines - 1);
        var starts = _wrap.Starts(line);
        var cells = Graphemes(line);
        var column = Math.Clamp(position.X, 0, cells.Count);
        return (_wrap.FirstRow(line) + WrapLayout.RowOf(starts, column), WrapLayout.X(cells, starts, column, TabWidth));
    }

    // ↑/↓ and the page keys step through screen rows, keeping the screen column the caret started from.
    private bool MoveByRows(Command[] commands)
    {
        var (rows, extend) = commands switch
        {
            [Command.Up] => (-1, false),
            [Command.UpExtend] => (-1, true),
            [Command.Down] => (1, false),
            [Command.DownExtend] => (1, true),
            [Command.PageUp] => (-Viewport.Height, false),
            [Command.PageUpExtend] => (-Viewport.Height, true),
            [Command.PageDown] => (Viewport.Height, false),
            [Command.PageDownExtend] => (Viewport.Height, true),
            _ => (0, false),
        };
        if (rows == 0) return false;

        RefreshWrap();
        var from = InsertionPoint;
        var (row, x) = Locate(from);
        if (_rowTrack is { } track && track.At == from) x = track.X;
        var (line, sub) = _wrap.At(row + rows);
        var to = new Point(WrapLayout.ColumnAt(Graphemes(line), _wrap.Starts(line), sub, x, TabWidth), line);

        if (rows is not (1 or -1)) ScrollToRow(Viewport.Y + rows);
        Load(extend ? new Caret(to, PrimaryCaret.Anchor ?? from, Extending: true) : new Caret(to), reveal: false);
        _rowTrack = (to, x);
        RaiseCursorMoved(this, null, null);
        return true;
    }

    private Point PositionAt(Point viewportPoint)
    {
        var (line, row) = _wrap.At(Viewport.Y + Math.Clamp(viewportPoint.Y, 0, Math.Max(Viewport.Height - 1, 0)));
        if (Viewport.Y + viewportPoint.Y >= _wrap.Rows) return new Point(GetLine(Lines - 1).Count, Lines - 1);
        var x = Math.Clamp(viewportPoint.X, 0, Math.Max(Viewport.Width - 1, 0));
        return new Point(WrapLayout.ColumnAt(Graphemes(line), _wrap.Starts(line), row, x, TabWidth), line);
    }

    private void DrawWrappedRows(int right, int bottom, ref int row)
    {
        var (line, sub) = _wrap.At(Viewport.Y);
        for (; line < Lines && row < bottom; line++, sub = 0)
        {
            var cells = GetLine(line);
            var starts = _wrap.Starts(line);
            for (; sub < starts.Length && row < bottom; sub++, row++)
                DrawRow(cells, line, row, right, (0, 0, starts[sub]), sub + 1 < starts.Length ? starts[sub + 1] : cells.Count);
        }
    }

    // TG's PositionCursor counts file lines, so the caret is placed again once the rows are drawn.
    private void PlaceWrappedCursor()
    {
        if (!CanFocus || !Enabled || ReadOnly)
        {
            Cursor = Cursor with { Position = null };
            return;
        }
        var (row, x) = Locate(InsertionPoint);
        var y = row - Viewport.Y;
        Cursor = Cursor with
        {
            Position = y >= 0 && y < Viewport.Height ? ViewportToScreen(new Point(Math.Min(x, Viewport.Width - 1), y)) : null,
        };
    }

    // Clicks and drags land on the row they're on; TG's own handling reads the row under the pointer as a file line.
    private bool OnWrappedMouse(Mouse mouse)
    {
        var flags = mouse.Flags;
        if (flags.HasFlag(MouseFlags.WheeledDown) || flags.HasFlag(MouseFlags.WheeledUp))
        {
            RefreshWrap();
            ScrollToRow(Viewport.Y + (flags.HasFlag(MouseFlags.WheeledDown) ? 1 : -1));
            return true;
        }
        if (flags.HasFlag(MouseFlags.WheeledLeft) || flags.HasFlag(MouseFlags.WheeledRight)) return true;

        const MouseFlags buttons = MouseFlags.LeftButtonPressed | MouseFlags.LeftButtonReleased | MouseFlags.LeftButtonClicked
                                   | MouseFlags.LeftButtonDoubleClicked | MouseFlags.LeftButtonTripleClicked;
        if ((flags & buttons) == 0 || mouse.Position is not { } point) return false;

        if (CanFocus && !HasFocus) SetFocus();
        RemoveSecondaryCarets();
        RefreshWrap();

        if (flags.HasFlag(MouseFlags.LeftButtonPressed) && flags.HasFlag(MouseFlags.PositionReport))
        {
            if (point.Y < 0) ScrollToRow(Viewport.Y - 1);
            else if (point.Y >= Viewport.Height) ScrollToRow(Viewport.Y + 1);
            var anchor = _dragAnchor ??= InsertionPoint;
            Load(new Caret(PositionAt(point), anchor, Extending: true), reveal: false);
        }
        else if (flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            Load(new Caret(PositionAt(point)), reveal: false);
            _dragAnchor = InsertionPoint;
            if (App is { } app && !app.Mouse.IsGrabbed(this)) app.Mouse.GrabMouse(this);
        }
        else if (flags.HasFlag(MouseFlags.LeftButtonReleased))
        {
            _dragAnchor = null;
            App?.Mouse.UngrabMouse();
        }
        else if (flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            if (!IsSelecting) Load(new Caret(PositionAt(point)), reveal: false);
        }
        else if (flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            var clicked = PositionAt(point);
            string[] line = [.. Graphemes(clicked.Y)];
            Load(Occurrences.RunAt([line], clicked with { Y = 0 }) is var (start, end)
                ? new Caret(end with { Y = clicked.Y }, start with { Y = clicked.Y }, Extending: true)
                : new Caret(clicked), reveal: false);
        }
        else
        {
            var row = PositionAt(point).Y;
            Load(new Caret(new Point(GetLine(row).Count, row), new Point(0, row), Extending: true), reveal: false);
        }

        _followed = (CurrentRow, CurrentColumn);
        RaiseCursorMoved(this, null, null);
        SetNeedsDraw();
        return true;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "RaiseUnwrappedCursorPositionChanged")]
    private static extern void RaiseCursorMoved(TextView view, int? row, int? column);
}
