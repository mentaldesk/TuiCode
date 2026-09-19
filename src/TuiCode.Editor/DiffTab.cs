using System.Diagnostics;
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
    private readonly TokenPalette _palette = new();
    private IReadOnlyList<string> _left = [];
    private IReadOnlyList<string> _right = [];
    private int _top;
    private int _current;
    private int _column;
    private int? _widest;

    public DiffTab(EditorTab source, string leftLabel, Func<IReadOnlyList<string>> readLeft, SyntaxHighlighter? syntax = null, string? leftKey = null)
    {
        Source = source;
        LeftLabel = leftLabel;
        LeftKey = leftKey ?? leftLabel;
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
        AddCommand(Command.ScrollLeft, () => ScrollSidewaysTo(_column - 1));
        AddCommand(Command.ScrollRight, () => ScrollSidewaysTo(_column + 1));
        AddCommand(Command.PageLeft, () => ScrollSidewaysTo(_column - LargeSidewaysStep));
        AddCommand(Command.PageRight, () => ScrollSidewaysTo(_column + LargeSidewaysStep));
        KeyBindings.Add(Key.CursorUp, Command.Up);
        KeyBindings.Add(Key.CursorDown, Command.Down);
        KeyBindings.Add(Key.CursorLeft, Command.ScrollLeft);
        KeyBindings.Add(Key.CursorRight, Command.ScrollRight);
        KeyBindings.Add(Key.CursorLeft.WithShift, Command.PageLeft);
        KeyBindings.Add(Key.CursorRight.WithShift, Command.PageRight);
        KeyBindings.Add(Key.PageUp, Command.PageUp);
        KeyBindings.Add(Key.PageDown, Command.PageDown);
        KeyBindings.Add(Key.Home, Command.Start);
        KeyBindings.Add(Key.End, Command.End);
        KeyBindings.Add(Key.Home.WithCtrl, Command.Start);
        KeyBindings.Add(Key.End.WithCtrl, Command.End);
        MouseBindings.Add(MouseFlags.WheeledUp, Command.ScrollUp);
        MouseBindings.Add(MouseFlags.WheeledDown, Command.ScrollDown);
        MouseBindings.Add(MouseFlags.WheeledLeft, Command.ScrollLeft);
        MouseBindings.Add(MouseFlags.WheeledRight, Command.ScrollRight);
        UpdateTitle();
    }

    public EditorTab Source { get; }

    /// <summary>What the left side is, e.g. <c>saved</c>.</summary>
    public string LeftLabel { get; }

    /// <summary>Identifies the left side among the source's diffs, e.g. a file's full path.</summary>
    public string LeftKey { get; }

    public AlignedDiff Diff { get; private set; }

    public int TopRow => _top;

    /// <summary>How many columns both sides are scrolled right.</summary>
    public int LeftColumn => _column;

    // Focus sits in the tab header, and HasFocus stays true after it moves on, so ask the app what's focused.
    public bool IsFocused => App?.Navigation?.GetFocused() is { } focused && IsInHierarchy(this, focused, includeAdornments: true);

    public int CurrentRow => _current;

    internal LineTokenCache? LeftTokens { get; private set; }

    internal LineTokenCache? RightTokens { get; private set; }

    /// <summary>Lexing time allowed per frame, across both sides; the rest continues on later iterations.</summary>
    internal TimeSpan SyntaxBudget { get; set; } = TimeSpan.FromMilliseconds(15);

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
        _widest = null;
        if (!Equals(LeftTokens?.Language, Source.Grammar))
        {
            LeftTokens = _syntax?.CreateCache(Source.Grammar);
            RightTokens = _syntax?.CreateCache(Source.Grammar);
        }
        LeftTokens?.Update(_left);
        RightTokens?.Update(_right);
        UpdateTitle();
        ScrollTo(_top);
        if (_column > 0) ScrollSidewaysTo(_column);
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
        file.FileSystem.File.Exists(file.FullName) ? SplitLines(file.FileSystem.File.ReadAllText(file.FullName)) : [];

    /// <summary>Text split into lines as the editor would load it.</summary>
    public static IReadOnlyList<string> SplitLines(string text) =>
        Cell.StringToLinesOfCells(text).Select(Cell.ToString).ToArray();

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

    private bool ScrollSidewaysTo(int column)
    {
        _widest ??= _left.Concat(_right).Select(line => Columns(line)).DefaultIfEmpty().Max();
        _column = Math.Clamp(column, 0, Math.Max(0, _widest.Value - NarrowerTextWidth));
        SetNeedsDraw();
        return true;
    }

    private int Digits => Math.Max(MinDigits, Math.Max(_left.Count, _right.Count).ToString().Length);

    private (int Left, int Right) SideWidths()
    {
        var left = Math.Max(0, Viewport.Width - 1) / 2;
        return (left, Math.Max(0, Viewport.Width - left - 1));
    }

    private int NarrowerTextWidth => Math.Max(0, SideWidths().Left - Digits - 2);

    private int LargeSidewaysStep => Math.Max(1, NarrowerTextWidth / 4);

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = GetAttributeForRole(VisualRole.Editable);
        var removed = normal with { Background = ThemeColor("diffEditor.removedLineBackground") ?? DefaultRemoved };
        var inserted = normal with { Background = ThemeColor("diffEditor.insertedLineBackground") ?? DefaultInserted };
        var (leftWidth, rightWidth) = SideWidths();
        var rightX = leftWidth + 1;
        var digits = Digits;

        var current = GetAttributeForRole(VisualRole.Focus);
        var header = normal with { Style = normal.Style | TextStyle.Bold };
        PrepareSyntax();
        DrawText(0, 0, leftWidth, " " + LeftLabel, header);
        DrawSeparator(leftWidth, 0, normal);
        DrawText(rightX, 0, rightWidth, " working copy", header);

        for (var y = 1; y < Viewport.Height; y++)
        {
            var index = _top + y - 1;
            DiffRow? row = index < Diff.Rows.Count ? Diff.Rows[index] : null;
            var changed = row?.Kind is DiffRowKind.Modified or DiffRowKind.LeftOnly or DiffRowKind.RightOnly;
            Attribute? marked = row is not null && index == _current ? current : null;
            DrawSide(0, y, leftWidth, row?.Left, _left, LeftTokens, digits, changed ? '-' : ' ', changed ? removed : normal, normal, marked);
            DrawSeparator(leftWidth, y, normal);
            DrawSide(rightX, y, rightWidth, row?.Right, _right, RightTokens, digits, changed ? '+' : ' ', changed ? inserted : normal, normal, marked);
        }
        return true;
    }

    private void PrepareSyntax()
    {
        if (_syntax is null || (LeftTokens is null && RightTokens is null)) return;
        _palette.Sync(_syntax);
        var (lastLeft, lastRight) = (-1, -1);
        for (var i = _top; i < Math.Min(Diff.Rows.Count, _top + PageHeight); i++)
        {
            lastLeft = Diff.Rows[i].Left ?? lastLeft;
            lastRight = Diff.Rows[i].Right ?? lastRight;
        }
        var clock = Stopwatch.StartNew();
        var done = LeftTokens?.TokenizeThrough(lastLeft, SyntaxBudget) ?? true;
        done &= RightTokens?.TokenizeThrough(lastRight, SyntaxBudget - clock.Elapsed) ?? true;
        if (!done) App?.Invoke(SetNeedsDraw);
    }

    private void DrawSide(int x, int y, int width, int? line, IReadOnlyList<string> lines, LineTokenCache? tokens, int digits, char marker, Attribute tint, Attribute normal, Attribute? current)
    {
        var prefix = line is { } i ? $"{(i + 1).ToString().PadLeft(digits)}{marker} " : new string(' ', digits + 2);
        var number = marker == ' ' ? tint with { Style = tint.Style | TextStyle.Faint } : tint;
        var used = Math.Min(width, prefix.Length);
        DrawText(x, y, used, prefix, current ?? (line is null ? normal : number));
        if (line is { } l) DrawText(x + used, y, width - used, lines[l], tint, tokens?.TokensFor(l), _column);
        else DrawText(x + used, y, width - used, "", normal);
    }

    private void DrawSeparator(int x, int y, Attribute attribute)
    {
        if (x >= Viewport.Width) return;
        SetAttribute(attribute);
        AddStr(x, y, "│");
    }

    // Pads to width; the first `skip` columns and whatever passes width are cut. Token offsets are UTF-16 chars, not cells.
    private void DrawText(int x, int y, int width, string text, Attribute attribute, int[]? tokens = null, int skip = 0)
    {
        SetAttribute(attribute);
        var col = 0;
        var chars = 0;
        var token = -1;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext() && col - skip < width)
        {
            var grapheme = elements.GetTextElement();
            if (tokens is { Length: > 0 })
            {
                var next = Math.Max(token, 0);
                while (next + 2 < tokens.Length && tokens[next + 2] <= chars)
                    next += 2;
                if (next != token)
                {
                    SetAttribute(_palette.Apply(attribute, tokens[next + 1]));
                    token = next;
                }
                chars += grapheme.Length;
            }
            var cols = Columns(grapheme, col);
            if (grapheme == "\t" || col < skip)
            {
                for (var end = col + cols; col < end && col - skip < width; col++)
                    if (col >= skip) AddStr(x + col - skip, y, " ");
                continue;
            }
            if (col - skip + cols > width) break;
            AddStr(x + col - skip, y, grapheme);
            col += cols;
        }
        SetAttribute(attribute);
        for (col = Math.Max(col, skip); col - skip < width; col++)
            AddStr(x + col - skip, y, " ");
    }

    private int Columns(string text)
    {
        var col = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
            col += Columns(elements.GetTextElement(), col);
        return col;
    }

    private int Columns(string grapheme, int col) => grapheme == "\t"
        ? Source.Settings.IndentSize - col % Source.Settings.IndentSize
        : Math.Max(1, grapheme.GetColumns(false));

    private Color? ThemeColor(string key) =>
        _syntax?.EditorColors.TryGetValue(key, out var hex) == true && Color.TryParse(hex, out Color? color) ? color : null;
}
