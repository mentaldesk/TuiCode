using Point = System.Drawing.Point;

namespace TuiCode.Editor;

// Column select (#114): the extend commands sweep a rectangle of carets instead of one stream selection.
internal sealed partial class EditorTextView
{
    // These keep the box's column across rows too short to reach it; TG's own tracking drops it after one such row.
    private static readonly HashSet<Command> ColumnRowCommands =
        [Command.UpExtend, Command.DownExtend, Command.PageUpExtend, Command.PageDownExtend];

    private static readonly HashSet<Command> ColumnCommands =
    [
        .. ColumnRowCommands,
        Command.LeftExtend, Command.RightExtend, Command.WordLeftExtend, Command.WordRightExtend,
        Command.LeftStartExtend, Command.RightEndExtend,
    ];

    // The corners as the user swept them, before each row clamps them to its own length.
    private (Point Anchor, Point Active)? _box;

    /// <summary>Whether extending the selection sweeps a rectangle rather than a run of text.</summary>
    public bool ColumnSelect
    {
        get;
        set
        {
            field = value;
            _box = null;
        }
    }

    private bool ExtendColumnSelection(KeyBinding binding)
    {
        if (!binding.Commands.All(ColumnCommands.Contains)) return false;
        var (anchor, active) = _box ?? (PrimaryCaret.Anchor ?? InsertionPoint, InsertionPoint);

        // Move a lone caret from the active corner so TG decides where the movement lands.
        Load(new Caret(active), reveal: false);
        InvokeCommands(binding.Commands, binding);
        var column = binding.Commands.All(ColumnRowCommands.Contains) ? active.X : CurrentColumn;

        SelectColumns(anchor, new Point(column, CurrentRow));
        return true;
    }

    /// <summary>Puts a caret on every row between the corners, selecting the columns between them.</summary>
    private void SelectColumns(Point anchor, Point active)
    {
        anchor = anchor with { Y = Math.Clamp(anchor.Y, 0, Lines - 1) };
        _box = (anchor, active);

        var step = Math.Sign(anchor.Y - active.Y);
        List<Caret> carets = [];
        // From the active row, so its caret stays primary and the view follows it.
        for (var row = active.Y; ; row += step)
        {
            var length = GetLine(row).Count;
            var (start, end) = (Math.Min(anchor.X, length), Math.Min(active.X, length));
            carets.Add(start == end
                ? new Caret(new Point(end, row))
                : new Caret(new Point(end, row), new Point(start, row), Extending: true));
            if (row == anchor.Y) break;
        }
        SetCarets(carets);
    }
}
