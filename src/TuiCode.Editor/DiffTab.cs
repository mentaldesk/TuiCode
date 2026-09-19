using System.Globalization;
using Terminal.Gui.Text;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Editor;

/// <summary>A read-only side-by-side diff of another version (left) against an editor tab's live buffer (right).</summary>
public sealed class DiffTab : FrameView
{
    /// <summary>A revision can be far further from the buffer than the gutter's give-up limit allows.</summary>
    public const int MaxEdits = 5_000;

    private const int MinDigits = 3;
    private const int ChangeContext = 2;

    private static readonly Color DefaultRemoved = new(0x5A, 0x1E, 0x1E);
    private static readonly Color DefaultInserted = new(0x1E, 0x4A, 0x28);

    private readonly Func<IReadOnlyList<string>> _readLeft;
    private readonly SyntaxHighlighter? _syntax;
    private IReadOnlyList<string> _left = [];
    private IReadOnlyList<string> _right = [];
    private int _top;
    private int _current;

    public DiffTab(EditorTab source, string leftLabel, Func<IReadOnlyList<string>> readLeft, SyntaxHighlighter? syntax = null)
    {
        Source = source;
        LeftLabel = leftLabel;
        _readLeft = readLeft;
        _syntax = syntax;
        BorderStyle = LineStyle.None;
        CanFocus = true;
        Diff = AlignedDiff.Compute([], []);

        AddCommand(Command.Up, () => MoveTo(_current - 1));
        AddCommand(Command.Down, () => MoveTo(_current + 1));
        AddCommand(Command.PageUp, () => { ScrollTo(_top - PageHeight); return MoveTo(_current - PageHeight); });
        AddCommand(Command.PageDown, () => { ScrollTo(_top + PageHeight); return MoveTo(_current + PageHeight); });
        AddCommand(Command.Start, () => MoveTo(0));
        AddCommand(Command.End, () => MoveTo(int.MaxValue));
        AddCommand(Command.ScrollUp, () => ScrollTo(_top - 1));
        AddCommand(Command.ScrollDown, () => ScrollTo(_top + 1));
        KeyBindings.Add(Key.CursorUp, Command.Up);
        KeyBindings.Add(Key.CursorDown, Command.Down);
        KeyBindings.Add(Key.PageUp, Command.PageUp);
        KeyBindings.Add(Key.PageDown, Command.PageDown);
        KeyBindings.Add(Key.Home, Command.Start);
        KeyBindings.Add(Key.End, Command.End);
        KeyBindings.Add(Key.Home.WithCtrl, Command.Start);
        KeyBindings.Add(Key.End.WithCtrl, Command.End);
        MouseBindings.Add(MouseFlags.WheeledUp, Command.ScrollUp);
        MouseBindings.Add(MouseFlags.WheeledDown, Command.ScrollDown);
        UpdateTitle();
    }

    public EditorTab Source { get; }

    /// <summary>What the left side is, e.g. <c>saved</c>.</summary>
    public string LeftLabel { get; }

    public AlignedDiff Diff { get; private set; }

    public int TopRow => _top;

    // Focus sits in the tab header, and HasFocus stays true after it moves on, so ask the app what's focused.
    public bool IsFocused => App?.Navigation?.GetFocused() is { } focused && IsInHierarchy(this, focused, includeAdornments: true);

    public int CurrentRow => _current;

    /// <summary>The buffer line <see cref="CurrentRow"/> maps to.</summary>
    public int CurrentBufferLine => Diff.BufferLine(_current);

    /// <summary>The 1-based change block <see cref="CurrentRow"/> is in or below; 0 above the first.</summary>
    public int CurrentChange => Diff.ChangeBlocks.Count(start => start <= _current);

    public string ChangeStatus => (Diff.ChangeBlocks.Count, CurrentChange) switch
    {
        (0, _) => "No changes",
        (1, 0) => "1 change",
        (var count, 0) => $"{count} changes",
        var (count, current) => $"Change {current} of {count}",
    };

    /// <summary>Stops at the last change rather than wrapping.</summary>
    public void NextChange()
    {
        var start = Diff.ChangeBlocks.FirstOrDefault(s => s > _current, -1);
        if (start >= 0) ShowChange(start);
    }

    /// <summary>Stops at the first change rather than wrapping.</summary>
    public void PreviousChange()
    {
        var start = Diff.ChangeBlocks.LastOrDefault(s => s < _current, -1);
        if (start >= 0) ShowChange(start);
    }

    private void ShowChange(int start)
    {
        _current = start;
        ScrollTo(start - ChangeContext);
    }

    private int PageHeight => Math.Max(1, Viewport.Height - 1);

    /// <summary>Re-read both sides and recompute the diff.</summary>
    public void Refresh()
    {
        _left = _readLeft();
        _right = [.. Source.SnapshotLines];
        Diff = AlignedDiff.Compute(_left, _right, MaxEdits);
        UpdateTitle();
        ScrollTo(_top);
        MoveTo(_current);
    }

    internal void UpdateTitle()
    {
        Title = $"{Source.File.Name} ↔ {LeftLabel}";
        if (Border.View is BorderView { TitleView: ITitleView header }) header.MeasuredTabLength = 0;
        SetNeedsLayout();
    }

    /// <summary>The file's lines as the editor would load them.</summary>
    public static IReadOnlyList<string> ReadLines(IFileInfo file) =>
        file.FileSystem.File.Exists(file.FullName)
            ? Cell.StringToLinesOfCells(file.FileSystem.File.ReadAllText(file.FullName)).Select(Cell.ToString).ToArray()
            : [];

    private bool MoveTo(int row)
    {
        _current = Math.Clamp(row, 0, Math.Max(0, Diff.Rows.Count - 1));
        if (_current < _top) ScrollTo(_current);
        else if (_current >= _top + PageHeight) ScrollTo(_current - PageHeight + 1);
        SetNeedsDraw();
        return true;
    }

    private bool ScrollTo(int top)
    {
        var max = Math.Max(0, Diff.Rows.Count - PageHeight);
        _top = Math.Clamp(top, 0, max);
        SetNeedsDraw();
        return true;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = GetAttributeForRole(VisualRole.Editable);
        var removed = normal with { Background = ThemeColor("diffEditor.removedLineBackground") ?? DefaultRemoved };
        var inserted = normal with { Background = ThemeColor("diffEditor.insertedLineBackground") ?? DefaultInserted };
        var width = Viewport.Width;
        var leftWidth = Math.Max(0, width - 1) / 2;
        var rightX = leftWidth + 1;
        var rightWidth = Math.Max(0, width - rightX);
        var digits = Math.Max(MinDigits, Math.Max(_left.Count, _right.Count).ToString().Length);

        var current = GetAttributeForRole(VisualRole.Focus);
        var header = normal with { Style = normal.Style | TextStyle.Bold };
        DrawText(0, 0, leftWidth, " " + LeftLabel, header);
        DrawSeparator(leftWidth, 0, normal);
        DrawText(rightX, 0, rightWidth, " working copy", header);

        for (var y = 1; y < Viewport.Height; y++)
        {
            var index = _top + y - 1;
            DiffRow? row = index < Diff.Rows.Count ? Diff.Rows[index] : null;
            var changed = row?.Kind is DiffRowKind.Modified or DiffRowKind.LeftOnly or DiffRowKind.RightOnly;
            Attribute? marked = row is not null && index == _current ? current : null;
            DrawSide(0, y, leftWidth, row?.Left, _left, digits, changed ? '-' : ' ', changed ? removed : normal, normal, marked);
            DrawSeparator(leftWidth, y, normal);
            DrawSide(rightX, y, rightWidth, row?.Right, _right, digits, changed ? '+' : ' ', changed ? inserted : normal, normal, marked);
        }
        return true;
    }

    private void DrawSide(int x, int y, int width, int? line, IReadOnlyList<string> lines, int digits, char marker, Attribute tint, Attribute normal, Attribute? current)
    {
        var prefix = line is { } i ? $"{(i + 1).ToString().PadLeft(digits)}{marker} " : new string(' ', digits + 2);
        var number = marker == ' ' ? tint with { Style = tint.Style | TextStyle.Faint } : tint;
        var used = Math.Min(width, prefix.Length);
        DrawText(x, y, used, prefix, current ?? (line is null ? normal : number));
        DrawText(x + used, y, width - used, line is { } l ? lines[l] : "", line is null ? normal : tint);
    }

    private void DrawSeparator(int x, int y, Attribute attribute)
    {
        if (x >= Viewport.Width) return;
        SetAttribute(attribute);
        AddStr(x, y, "│");
    }

    // Pads to width; long lines are cut, not wrapped or scrolled.
    private void DrawText(int x, int y, int width, string text, Attribute attribute)
    {
        SetAttribute(attribute);
        var col = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var grapheme = elements.GetTextElement();
            if (grapheme == "\t")
            {
                var spaces = Source.Settings.IndentSize - col % Source.Settings.IndentSize;
                for (var i = 0; i < spaces && col < width; i++)
                    AddStr(x + col++, y, " ");
                continue;
            }
            var cols = Math.Max(1, grapheme.GetColumns(false));
            if (col + cols > width) break;
            AddStr(x + col, y, grapheme);
            col += cols;
        }
        for (; col < width; col++)
            AddStr(x + col, y, " ");
    }

    private Color? ThemeColor(string key) =>
        _syntax?.EditorColors.TryGetValue(key, out var hex) == true && Color.TryParse(hex, out Color? color) ? color : null;
}
