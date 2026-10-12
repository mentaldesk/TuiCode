using Terminal.Gui.Drivers;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace TuiCode.Editor;

// Soft wrap (#378); see "Word wrap" in AGENTS.md.
internal sealed partial class CodeEditor
{
    private readonly WrapMap _wrap = new();
    private (int Row, int Column)? _followed;
    private bool _barScrolling;
    private bool _followingBar;
    private Point? _dragAnchor;
    private Dictionary<Point, int> _rowTracks = [];

    public bool SoftWrap
    {
        get;
        set
        {
            if (field == value) return;
            var topLine = TopLine;
            field = value;
            _followed = null;
            _box = null;
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

    internal int TopLine => SoftWrap ? _wrap.At(Viewport.Y).Line : Viewport.Y;

    internal void ScrollToLine(int line, int column)
    {
        line = Math.Clamp(line, 0, Math.Max(Lines - 1, 0));
        if (SoftWrap)
        {
            RefreshWrap();
            ScrollToRow(_wrap.FirstRow(line));
            return;
        }
        Viewport = Viewport with { X = Math.Max(column, 0), Y = Math.Min(line, Math.Max(Lines - Viewport.Height, 0)) };
    }

    // TG positions the view by file line; while wrapped the caret's screen row decides that, at the next draw.
    public override Rectangle Viewport
    {
        get => base.Viewport;
        set
        {
            if (!SoftWrap)
            {
                // TG scrolls a view with no width yet sideways to "show" the caret, and nothing scrolls it back (#513).
                base.Viewport = base.Viewport.Width == 0 ? value with { X = base.Viewport.X } : value;
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
        if (Reveal.TopRow(Viewport.Y, Viewport.Height, _wrap.Rows, _wrap.FirstRow(first), lastRow, PinnedCountAt) is { } top)
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
        if (!_wrap.Update(_lines, Graphemes, WrapWidth, TabWidth)) return;

        SetContentSize(new Size(Viewport.Width, _wrap.Rows));
        if (resized) ScrollToRow(_wrap.FirstRow(top.Line) + Math.Min(top.Row, _wrap.Starts(Math.Min(top.Line, Lines - 1)).Length - 1));
    }

    private void FollowCaret()
    {
        RefreshWrap();
        var caret = (CurrentRow, CurrentColumn);
        if (_followed == caret) return;
        _followed = caret;
        ScrollRowIntoView(Locate(InsertionPoint).Row);
    }

    private void ScrollRowIntoView(int row)
    {
        if (row < Viewport.Y) ScrollToRow(row);
        else if (row >= Viewport.Y + Viewport.Height) ScrollToRow(row - Viewport.Height + 1);
    }

    private (int Row, int X) Locate(Point position)
    {
        var line = Math.Clamp(position.Y, 0, _wrap.Lines - 1);
        var starts = _wrap.Starts(line);
        var cells = Graphemes(line);
        var column = Math.Clamp(position.X, 0, cells.Length);
        return (_wrap.FirstRow(line) + WrapLayout.RowOf(starts, column), WrapLayout.X(cells, starts, column, TabWidth));
    }

    // ↑/↓ and the page keys step through screen rows, keeping the screen column the caret started from.
    private bool MoveByRows(Command[] commands)
    {
        if (VerticalRows(commands) is not var (rows, extend)) return false;

        RefreshWrap();
        if (rows is not (1 or -1)) ScrollToRow(Viewport.Y + rows);
        Dictionary<Point, int> tracks = [];
        if (HasSecondaryCarets) AtEachCaret(_ => MoveCaretByRows(rows, extend, tracks));
        else MoveCaretByRows(rows, extend, tracks);
        _rowTracks = tracks;
        CursorMoved?.Invoke(this, InsertionPoint);
        return true;
    }

    private void MoveCaretByRows(int rows, bool extend, Dictionary<Point, int> tracks)
    {
        var from = InsertionPoint;
        var (row, x) = Locate(from);
        if (_rowTracks.TryGetValue(from, out var tracked)) x = tracked;
        var (line, sub) = _wrap.At(row + rows);
        var to = new Point(WrapLayout.ColumnAt(Graphemes(line), _wrap.Starts(line), sub, x, TabWidth), line);
        Load(extend ? new Caret(to, PrimaryCaret.Anchor ?? from) : new Caret(to), reveal: false);
        tracks[to] = x;
    }

    private Point PositionAt(Point viewportPoint)
    {
        if (!SoftWrap)
        {
            var line = Math.Clamp(Viewport.Y + viewportPoint.Y, 0, Lines - 1);
            return new Point(WrapLayout.ColumnAt(Graphemes(line), [0], 0, Math.Max(Viewport.X + viewportPoint.X, 0), TabWidth), line);
        }
        if (Viewport.Y + viewportPoint.Y >= _wrap.Rows) return new Point(LineLength(Lines - 1), Lines - 1);
        var row = Viewport.Y + Math.Clamp(viewportPoint.Y, 0, Math.Max(Viewport.Height - 1, 0));
        return PositionOnRow(row, Math.Clamp(viewportPoint.X, 0, Math.Max(Viewport.Width - 1, 0)));
    }

    private Point PositionOnRow(int screenRow, int x)
    {
        var (line, sub) = _wrap.At(screenRow);
        return new Point(WrapLayout.ColumnAt(Graphemes(line), _wrap.Starts(line), sub, x, TabWidth), line);
    }

    private Point ScreenPoint(Point position)
    {
        var (row, x) = Locate(position);
        return new Point(x, row);
    }

    // Column select while wrapped sweeps a box of screen rows and cells, as the text is laid out on screen.
    private bool ExtendWrappedColumnSelection(KeyBinding binding)
    {
        RefreshWrap();
        var (anchor, active) = _box ?? (ScreenPoint(PrimaryCaret.Anchor ?? InsertionPoint), ScreenPoint(InsertionPoint));
        var rows = binding.Commands switch
        {
            [Command.UpExtend] => -1,
            [Command.DownExtend] => 1,
            [Command.PageUpExtend] => -Viewport.Height,
            [Command.PageDownExtend] => Viewport.Height,
            _ => 0,
        };
        if (rows != 0)
            active = active with { Y = Math.Clamp(active.Y + rows, 0, _wrap.Rows - 1) };
        else if (binding.Commands is [Command.LeftStartExtend])
            active = active with { X = 0 };
        else if (binding.Commands is [Command.RightEndExtend])
            active = active with { X = int.MaxValue };
        else
        {
            Load(new Caret(PositionOnRow(active.Y, active.X)), reveal: false);
            foreach (var command in binding.Commands)
                InvokeCommand(command);
            active = ScreenPoint(InsertionPoint);
        }

        _box = (anchor, active);
        var step = Math.Sign(anchor.Y - active.Y);
        List<Caret> carets = [];
        // From the active row, so its caret stays primary and the view follows it.
        for (var row = active.Y; ; row += step)
        {
            var (start, end) = (PositionOnRow(row, anchor.X), PositionOnRow(row, active.X));
            carets.Add(start == end ? new Caret(end) : new Caret(end, start));
            if (row == anchor.Y) break;
        }
        SetCarets(carets);
        return true;
    }

    private void DrawWrappedRows(int right, int bottom, ref int row)
    {
        var (line, sub) = _wrap.At(Viewport.Y);
        for (; line < Lines && row < bottom; line++, sub = 0)
        {
            var cells = Graphemes(line);
            var starts = _wrap.Starts(line);
            for (; sub < starts.Length && row < bottom; sub++, row++)
                DrawRow(cells, line, row, right, (0, 0, starts[sub]), sub + 1 < starts.Length ? starts[sub + 1] : cells.Length);
        }
    }

    // Clicks and drags land on the row they're on, wrapped or not, and Alt+Click adds or removes a caret.
    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (OnPinnedMouse(mouse)) return true;
        var flags = mouse.Flags;
        if (SoftWrap && (flags.HasFlag(MouseFlags.WheeledDown) || flags.HasFlag(MouseFlags.WheeledUp)))
        {
            RefreshWrap();
            ScrollToRow(Viewport.Y + (flags.HasFlag(MouseFlags.WheeledDown) ? 1 : -1));
            return true;
        }
        if (SoftWrap && (flags.HasFlag(MouseFlags.WheeledLeft) || flags.HasFlag(MouseFlags.WheeledRight))) return true;

        const MouseFlags buttons = MouseFlags.LeftButtonPressed | MouseFlags.LeftButtonReleased | MouseFlags.LeftButtonClicked
                                   | MouseFlags.LeftButtonDoubleClicked | MouseFlags.LeftButtonTripleClicked;
        if ((flags & buttons) == 0 || mouse.Position is not { } point) return base.OnMouseEvent(mouse);

        if (CanFocus && !HasFocus) SetFocus();
        if (SoftWrap) RefreshWrap();
        if (flags.HasFlag(MouseFlags.Alt))
        {
            if (flags.HasFlag(MouseFlags.LeftButtonClicked)) ToggleCaret(Carets, PositionAt(point));
            return true;
        }
        RemoveSecondaryCarets();
        var before = InsertionPoint;

        if (flags.HasFlag(MouseFlags.LeftButtonPressed) && flags.HasFlag(MouseFlags.PositionReport))
        {
            if (point.Y < 0) ScrollRows(-1);
            else if (point.Y >= Viewport.Height) ScrollRows(1);
            var anchor = _dragAnchor ??= InsertionPoint;
            Load(new Caret(PositionAt(point), anchor), reveal: false);
        }
        else if (flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            var anchor = flags.HasFlag(MouseFlags.Shift) ? PrimaryCaret.Anchor ?? InsertionPoint : (Point?)null;
            Load(new Caret(PositionAt(point), anchor), reveal: false);
            _dragAnchor = anchor ?? InsertionPoint;
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
            Load(Occurrences.RunAt([Graphemes(clicked.Y)], clicked with { Y = 0 }) is var (start, end)
                ? new Caret(end with { Y = clicked.Y }, start with { Y = clicked.Y })
                : new Caret(clicked), reveal: false);
        }
        else
        {
            var row = PositionAt(point).Y;
            Load(new Caret(new Point(LineLength(row), row), new Point(0, row)), reveal: false);
        }

        _followed = (CurrentRow, CurrentColumn);
        ReportMove(before);
        SetNeedsDraw();
        return true;
    }

    private void ScrollRows(int rows)
    {
        if (SoftWrap) ScrollToRow(Viewport.Y + rows);
        else Viewport = Viewport with { Y = Math.Clamp(Viewport.Y + rows, 0, Math.Max(Lines - Viewport.Height, 0)) };
    }
}
