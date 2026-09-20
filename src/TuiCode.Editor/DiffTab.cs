using System.Diagnostics;
using System.Globalization;
using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Editor;

/// <summary>
/// A read-only side-by-side diff of another version (left) against an editor tab's live buffer (right),
/// or, for a file deleted in this branch (#182), against nothing.
/// </summary>
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
    private readonly SyntaxLanguage? _deletedGrammar;
    private readonly TokenPalette _palette = new();
    private readonly IFileInfo _file;
    private EditorSettings _settings = EditorSettings.Default;
    private IReadOnlyList<string> _left = [];
    private IReadOnlyList<string> _right = [];
    private IReadOnlyList<GitHubReviewThread> _threads = [];
    private readonly HashSet<GitHubReviewThread> _expanded = [];
    private List<Row> _rows = [];
    private int _top;
    private int _current;
    private int _column;
    private int? _widest;

    public DiffTab(EditorTab source, string leftLabel, Func<IReadOnlyList<string>> readLeft, SyntaxHighlighter? syntax = null, string? leftKey = null)
        : this(source.File, source, leftLabel, readLeft, syntax, leftKey)
    {
    }

    /// <summary>A file deleted in this branch (#182): the base version on the left, nothing on the right.</summary>
    public DiffTab(IFileInfo file, string leftLabel, Func<IReadOnlyList<string>> readLeft, SyntaxHighlighter? syntax = null, string? leftKey = null)
        : this(file, null, leftLabel, readLeft, syntax, leftKey)
    {
    }

    private DiffTab(IFileInfo file, EditorTab? source, string leftLabel, Func<IReadOnlyList<string>> readLeft, SyntaxHighlighter? syntax, string? leftKey)
    {
        _file = file;
        Source = source;
        LeftLabel = leftLabel;
        LeftKey = leftKey ?? leftLabel;
        _readLeft = readLeft;
        _syntax = syntax;
        if (source is null) _deletedGrammar = syntax?.LanguageForFile(file.Name);
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
        AddCommand(Command.ScrollLeft, () => ScrollSideways(-1));
        AddCommand(Command.ScrollRight, () => ScrollSideways(1));
        AddCommand(Command.PageLeft, () => ScrollSideways(-1, page: true));
        AddCommand(Command.PageRight, () => ScrollSideways(1, page: true));
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

    /// <summary>The file the diff is about; the deleted path when there's no <see cref="Source"/>.</summary>
    public IFileInfo File => Source?.File ?? _file;

    /// <summary>The live buffer on the right, or null for a file deleted in this branch (#182).</summary>
    public EditorTab? Source { get; }

    /// <summary>Whether this is a deleted file's diff, with nothing on the right (#182).</summary>
    public bool IsDeleted => Source is null;

    /// <summary>Indentation and line endings; a deleted file has no buffer to take them from.</summary>
    public EditorSettings Settings
    {
        get => Source?.Settings ?? _settings;
        set => _settings = value;
    }

    /// <summary>What the left side is, e.g. <c>saved</c>.</summary>
    public string LeftLabel { get; }

    /// <summary>Identifies the left side among the source's diffs, e.g. a file's full path.</summary>
    public string LeftKey { get; }

    /// <summary>Which file of a review this diff shows (#181); null when it was opened any other way.</summary>
    public ReviewSpot? Review { get; set; }

    public AlignedDiff Diff { get; private set; }

    /// <summary>A row on screen: a row of the diff, or a row of a review thread sitting under one (#186).</summary>
    private readonly record struct Row(int Diff, GitHubReviewThread? Thread, ThreadRow Text);

    /// <summary>The review threads shown under the lines they're on, top to bottom (#186).</summary>
    public IReadOnlyList<GitHubReviewThread> Threads => [.. _rows.Select(r => r.Thread).OfType<GitHubReviewThread>().Distinct()];

    /// <summary>The thread the current row belongs to, or null on a row of the diff itself.</summary>
    public GitHubReviewThread? CurrentThread => _current < _rows.Count ? _rows[_current].Thread : null;

    /// <summary>
    /// Shows <paramref name="threads"/> under the lines they're on (#186). A thread whose line is gone
    /// has no row here — the Review tab lists it as outdated instead.
    /// </summary>
    public void ShowThreads(IReadOnlyList<GitHubReviewThread> threads)
    {
        _threads = threads;
        _expanded.RemoveWhere(thread => !threads.Contains(thread));
        BuildRows();
        MoveTo(_current);
    }

    /// <summary>Whether a thread's comments are showing rather than just its first line (#186).</summary>
    public bool IsExpanded(GitHubReviewThread thread) => _expanded.Contains(thread);

    /// <summary>Expands or collapses the thread on the current row; false when it isn't a thread row (#186).</summary>
    public bool ToggleThread()
    {
        if (CurrentThread is not { } thread) return false;
        if (!_expanded.Add(thread)) _expanded.Remove(thread);
        BuildRows();
        MoveTo(_rows.FindIndex(r => ReferenceEquals(r.Thread, thread)));
        return true;
    }

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
    public int CurrentBufferLine => Diff.BufferLine(CurrentDiffRow);

    /// <summary>The 1-based change block <see cref="CurrentRow"/> is in or below; 0 above the first.</summary>
    public int CurrentChange => Diff.ChangeBlocks.Count(start => start <= CurrentDiffRow);

    /// <summary>The row of the diff the current row is, or the one a thread row sits under.</summary>
    private int CurrentDiffRow => _current < _rows.Count ? _rows[_current].Diff : _current;

    public string ChangeStatus => (Diff.ChangeBlocks.Count, CurrentChange) switch
    {
        (0, _) => "No changes",
        (1, 0) => "1 change",
        (var count, 0) => $"{count} changes",
        var (count, current) => $"Change {current} of {count}",
    };

    /// <summary>Stops at the last change rather than wrapping; false when it's already there.</summary>
    public bool NextChange() => ShowChange(Diff.ChangeBlocks.FirstOrDefault(s => s > CurrentDiffRow, -1));

    /// <summary>Stops at the first change rather than wrapping; false when it's already there.</summary>
    public bool PreviousChange() => ShowChange(Diff.ChangeBlocks.LastOrDefault(s => s < CurrentDiffRow, -1));

    /// <summary>Puts the first change in view; false when there are none.</summary>
    public bool FirstChange() => ShowChange(Diff.ChangeBlocks.FirstOrDefault(-1));

    /// <summary>Puts the last change in view; false when there are none.</summary>
    public bool LastChange() => ShowChange(Diff.ChangeBlocks.LastOrDefault(-1));

    /// <summary>Scrolls both sides sideways by one column, or by a quarter of the narrower side.</summary>
    public bool ScrollSideways(int direction, bool page = false) =>
        ScrollSidewaysTo(_column + direction * (page ? LargeSidewaysStep : 1));

    private bool ShowChange(int start)
    {
        if (start < 0) return false;
        // Change blocks count rows of the diff; thread rows (#186) sit between them and are stepped over.
        _current = Math.Max(0, _rows.FindIndex(r => r.Diff == start && r.Thread is null));
        ScrollTo(_current - ChangeContext);
        return true;
    }

    private int PageHeight => Math.Max(1, Viewport.Height - 1);

    /// <summary>Re-read both sides and recompute the diff.</summary>
    public void Refresh()
    {
        _left = _readLeft();
        _right = Source is { } source ? [.. source.SnapshotLines] : [];
        Diff = AlignedDiff.Compute(_left, _right, MaxEdits);
        _widest = null;
        var grammar = Source?.Grammar ?? _deletedGrammar;
        if (!Equals(LeftTokens?.Language, grammar))
        {
            LeftTokens = _syntax?.CreateCache(grammar);
            RightTokens = _syntax?.CreateCache(grammar);
        }
        LeftTokens?.Update(_left);
        RightTokens?.Update(_right);
        BuildRows();
        UpdateTitle();
        ScrollTo(_top);
        if (_column > 0) ScrollSidewaysTo(_column);
        MoveTo(_current);
    }

    internal void UpdateTitle()
    {
        Title = IsDeleted ? $"{File.Name} ↔ {LeftLabel} (deleted)" : $"{File.Name} ↔ {LeftLabel}";
        if (Border.View is BorderView { TitleView: ITitleView header }) header.MeasuredTabLength = 0;
        SetNeedsLayout();
    }

    /// <summary>The file's lines as the editor would load them.</summary>
    public static IReadOnlyList<string> ReadLines(IFileInfo file) =>
        file.FileSystem.File.Exists(file.FullName) ? SplitLines(file.FileSystem.File.ReadAllText(file.FullName)) : [];

    /// <summary>Text split into lines as the editor would load it.</summary>
    public static IReadOnlyList<string> SplitLines(string text) =>
        Cell.StringToLinesOfCells(text).Select(Cell.ToString).ToArray();

    private void BuildRows()
    {
        var rightRows = new Dictionary<int, int>();
        for (var i = 0; i < Diff.Rows.Count; i++)
            if (Diff.Rows[i].Right is { } right)
                rightRows.TryAdd(right, i);

        var byRow = new Dictionary<int, List<GitHubReviewThread>>();
        foreach (var thread in _threads)
        {
            if (thread.Outdated || thread.Line is not { } line || !rightRows.TryGetValue(line - 1, out var row)) continue;
            if (!byRow.TryGetValue(row, out var threads)) byRow[row] = threads = [];
            threads.Add(thread);
        }

        _rows = [];
        for (var i = 0; i < Diff.Rows.Count; i++)
        {
            _rows.Add(new Row(i, null, default));
            if (!byRow.TryGetValue(i, out var threads)) continue;
            foreach (var thread in threads)
                foreach (var text in ReviewThreadRows.For(thread, _expanded.Contains(thread)))
                    _rows.Add(new Row(i, thread, text));
        }
    }

    private bool MoveTo(int row)
    {
        _current = Math.Clamp(row, 0, Math.Max(0, _rows.Count - 1));
        if (_current < _top) ScrollTo(_current);
        else if (_current >= _top + PageHeight) ScrollTo(_current - PageHeight + 1);
        SetNeedsDraw();
        return true;
    }

    private bool ScrollTo(int top)
    {
        var max = Math.Max(0, _rows.Count - PageHeight);
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
        DrawText(rightX, 0, rightWidth, IsDeleted ? " deleted" : " working copy", header);

        for (var y = 1; y < Viewport.Height; y++)
        {
            var index = _top + y - 1;
            if (index < _rows.Count && _rows[index] is { Thread: { } thread } threadRow)
            {
                var faint = normal with { Style = normal.Style | TextStyle.Faint };
                DrawThread(y, threadRow.Text, index == _current ? current : thread.Resolved ? faint : normal);
                continue;
            }
            DiffRow? row = index < _rows.Count ? Diff.Rows[_rows[index].Diff] : null;
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
        for (var i = _top; i < Math.Min(_rows.Count, _top + PageHeight); i++)
        {
            var row = Diff.Rows[_rows[i].Diff];
            lastLeft = row.Left ?? lastLeft;
            lastRight = row.Right ?? lastRight;
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

    /// <summary>A thread row spans both panes, with its reply count at the right.</summary>
    private void DrawThread(int y, ThreadRow row, Attribute attribute)
    {
        var trailing = row.Trailing is { Length: > 0 } t ? $" {t}" : string.Empty;
        var textWidth = Math.Max(0, Viewport.Width - trailing.Length);
        DrawText(0, y, textWidth, row.Text, attribute);
        if (trailing.Length > 0) DrawText(textWidth, y, trailing.Length, trailing, attribute);
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
        ? Settings.IndentSize - col % Settings.IndentSize
        : Math.Max(1, grapheme.GetColumns(false));

    private Color? ThemeColor(string key) =>
        _syntax?.EditorColors.TryGetValue(key, out var hex) == true && Color.TryParse(hex, out Color? color) ? color : null;
}
