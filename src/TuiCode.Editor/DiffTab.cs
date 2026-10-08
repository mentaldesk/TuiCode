using System.Diagnostics;
using System.Globalization;
using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Editor;

/// <summary>
/// A read-only side-by-side diff of another version (left) against an editor tab's live buffer (right),
/// against nothing for a file deleted in this branch (#182), or against a later revision (#331).
/// </summary>
public sealed class DiffTab : FrameView
{
    /// <summary>A revision can be far further from the buffer than the gutter's give-up limit allows.</summary>
    public const int MaxEdits = 5_000;

    private const int MinDigits = 3;

    private static readonly Color DefaultRemoved = new(0x5A, 0x1E, 0x1E);
    private static readonly Color DefaultInserted = new(0x1E, 0x4A, 0x28);
    private static readonly Color DefaultComment = new(0x3A, 0x2C, 0x50);
    internal static readonly Color DefaultRemovedText = new(0x8A, 0x2E, 0x2E);
    internal static readonly Color DefaultInsertedText = new(0x2E, 0x72, 0x3E);

    private readonly Func<IReadOnlyList<string>> _readLeft;
    private readonly SyntaxHighlighter? _syntax;
    private readonly SyntaxLanguage? _ownGrammar;
    private readonly IReadOnlyList<string>? _rightRevision;
    private readonly TokenPalette _palette = new();
    private readonly IFileInfo _file;
    private readonly ScrollBar _scrollBar;
    private readonly ScrollBar _sidewaysBar;
    private bool _syncingScrollBar;
    private EditorSettings _settings = EditorSettings.Default;
    private IReadOnlyList<string> _left = [];
    private IReadOnlyList<string> _right = [];
    private IReadOnlyList<GitHubReviewThread> _threads = [];
    private IReadOnlyList<DraftComment> _drafts = [];
    private readonly HashSet<GitHubReviewThread> _expanded = [];
    private List<Row> _rows = [];
    private List<GitHubReviewThread> _placed = [];
    private List<DraftComment> _placedDrafts = [];
    private int _top;
    private int _current;
    private (int Start, int End)? _reverted;
    private int _column;
    private int? _widest;
    private View? _header;
    private Dictionary<(int Row, DiffSide Side), TextRange[]> _found = new();
    private DiffMatch? _currentMatch;

    public DiffTab(EditorTab source, string leftLabel, Func<IReadOnlyList<string>> readLeft, SyntaxHighlighter? syntax = null, string? leftKey = null)
        : this(source.File, source, leftLabel, readLeft, syntax, leftKey)
    {
    }

    /// <summary>A file deleted in this branch (#182): the base version on the left, nothing on the right.</summary>
    public DiffTab(IFileInfo file, string leftLabel, Func<IReadOnlyList<string>> readLeft, SyntaxHighlighter? syntax = null, string? leftKey = null)
        : this(file, null, leftLabel, readLeft, syntax, leftKey)
    {
    }

    /// <summary>A file as one commit changed it (#331): <paramref name="left"/> before, <paramref name="right"/> after, neither editable.</summary>
    public DiffTab(IFileInfo file, string leftLabel, IReadOnlyList<string> left, string rightLabel, IReadOnlyList<string> right, SyntaxHighlighter? syntax = null)
        : this(file, null, leftLabel, () => left, syntax, $"{leftLabel} ↔ {rightLabel}")
    {
        RightLabel = rightLabel;
        _rightRevision = right;
        UpdateTitle();
    }

    private DiffTab(IFileInfo file, EditorTab? source, string leftLabel, Func<IReadOnlyList<string>> readLeft, SyntaxHighlighter? syntax, string? leftKey)
    {
        _file = file;
        Source = source;
        LeftLabel = leftLabel;
        LeftKey = leftKey ?? leftLabel;
        _readLeft = readLeft;
        _syntax = syntax;
        if (source is null) _ownGrammar = syntax?.LanguageForFile(file.Name);
        BorderStyle = LineStyle.None;
        CanFocus = true;
        Diff = AlignedDiff.Compute([], []);

        _scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            X = Pos.AnchorEnd(),
            Y = Pos.Func(_ => HeaderHeight),
            Height = Dim.Fill(Dim.Func(_ => Padding!.Thickness.Bottom)),
            VisibilityMode = ScrollBarVisibilityMode.Auto,
        };
        _scrollBar.VisibleChanged += (_, _) => Padding!.Thickness = Padding.Thickness with { Right = _scrollBar.Visible ? 1 : 0 };
        _scrollBar.ValueChanged += (_, e) =>
        {
            if (!_syncingScrollBar) ScrollTo(e.NewValue);
        };
        _sidewaysBar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(Dim.Func(_ => Padding!.Thickness.Right)),
            VisibilityMode = ScrollBarVisibilityMode.Auto,
        };
        _sidewaysBar.VisibleChanged += (_, _) => Padding!.Thickness = Padding.Thickness with { Bottom = _sidewaysBar.Visible ? 1 : 0 };
        _sidewaysBar.ValueChanged += (_, e) =>
        {
            if (!_syncingScrollBar) ScrollSidewaysTo(FromSidewaysBar(e.NewValue));
        };
        ThinScrollBar.Apply(_sidewaysBar);
        Padding!.GetOrCreateView().Add(_scrollBar, _sidewaysBar);
        ViewportChanged += (_, _) => SyncScrollBar();

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
    public bool IsDeleted => Source is null && RightLabel is null;

    /// <summary>The revision on the right of a diff between two revisions (#331); null when the right is the buffer or nothing.</summary>
    public string? RightLabel { get; }

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

    /// <summary>The version on the left; for a deleted file (#182) it's what restoring it brings back (#247).</summary>
    public IReadOnlyList<string> LeftLines => _left;

    /// <summary>The version on the right: the buffer as last refreshed, the later revision, or nothing for a deleted file.</summary>
    public IReadOnlyList<string> RightLines => _right;

    /// <summary>Raised after <see cref="Refresh"/> recomputes the diff.</summary>
    public event EventHandler? Refreshed;

    public AlignedDiff Diff { get; private set; }

    /// <summary>The changed words on each side of each edited row, by row of <see cref="Diff"/> (#369).</summary>
    internal IReadOnlyDictionary<int, (TextRange[] Left, TextRange[] Right)> WordChanges { get; private set; } =
        new Dictionary<int, (TextRange[] Left, TextRange[] Right)>();

    /// <summary>A row on screen: a row of the diff, or a row of a thread (#186) or draft (#188) sitting under one.</summary>
    private readonly record struct Row(int Diff, GitHubReviewThread? Thread, DraftComment? Draft, ThreadRow Text)
    {
        public bool IsComment => Thread is not null || Draft is not null;
    }

    /// <summary>The review threads shown under the lines they're on, top to bottom (#186).</summary>
    public IReadOnlyList<GitHubReviewThread> Threads => _placed;

    /// <summary>The thread the current row belongs to, or null on a row of the diff itself.</summary>
    public GitHubReviewThread? CurrentThread => _current < _rows.Count ? _rows[_current].Thread : null;

    /// <summary>The draft comments shown under the lines they're on (#188).</summary>
    public IReadOnlyList<DraftComment> Drafts => _placedDrafts;

    /// <summary>The draft on the current row, or null when it isn't a draft row (#188).</summary>
    public DraftComment? CurrentDraft => _current < _rows.Count ? _rows[_current].Draft : null;

    /// <summary>
    /// The line the current row is on as GitHub numbers the head side (#188), or null on a row that's
    /// only on the left. A thread or draft row takes the line it sits under.
    /// </summary>
    public int? CurrentHeadLine =>
        _current < _rows.Count && Diff.Rows[_rows[_current].Diff].Right is { } right ? right + 1 : null;

    /// <summary>Shows <paramref name="drafts"/> under the lines they're on (#188).</summary>
    public void ShowDrafts(IReadOnlyList<DraftComment> drafts)
    {
        _drafts = drafts;
        BuildRows();
        MoveTo(_current);
    }

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

    /// <summary>
    /// Swaps <paramref name="thread"/> for the same thread with a reply on it (#189), keeping it expanded
    /// and current if it was. A diff that isn't showing it is left alone.
    /// </summary>
    public void ReplaceThread(GitHubReviewThread thread, GitHubReviewThread updated)
    {
        var threads = _threads.ToList();
        var index = threads.IndexOf(thread);
        if (index < 0) return;

        var wasCurrent = ReferenceEquals(CurrentThread, thread);
        threads[index] = updated;
        _threads = threads;
        if (_expanded.Remove(thread)) _expanded.Add(updated);
        BuildRows();
        var row = wasCurrent ? _rows.FindIndex(r => ReferenceEquals(r.Thread, updated)) : -1;
        MoveTo(row < 0 ? _current : row);
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

    /// <summary>
    /// The buffer lines a jump from <see cref="CurrentRow"/> should reveal (#287): its change block's,
    /// or just <see cref="CurrentBufferLine"/> on a row that sits between blocks or in one the buffer
    /// has no lines in.
    /// </summary>
    public (int First, int Last) CurrentChangeLines
    {
        get
        {
            var line = CurrentBufferLine;
            if (CurrentChange == 0) return (line, line);

            var block = Diff.Block(Diff.ChangeBlocks[CurrentChange - 1]);
            if (CurrentDiffRow > block.LastRow || block.RightCount == 0) return (line, line);
            // A block whose remaining rows are all left-only maps the current row past its own last line.
            return (Math.Min(block.RightStart, line), Math.Max(block.RightStart + block.RightCount - 1, line));
        }
    }

    /// <summary>The row of the diff the current row is, or the one a thread row sits under.</summary>
    public int CurrentDiffRow => _current < _rows.Count ? _rows[_current].Diff : _current;

    public string ChangeStatus => (Diff.ChangeBlocks.Count, CurrentChange) switch
    {
        (0, _) => "No changes",
        (1, 0) => "1 change",
        (var count, 0) => $"{count} changes",
        var (count, current) => $"Change {current} of {count}",
    };

    /// <summary>The diff's added and removed line counts, e.g. <c>+96 −21</c>, leaving out a side with none.</summary>
    public string LineCounts => string.Join(' ', new[]
    {
        Diff.AddedLines > 0 ? $"+{Diff.AddedLines}" : null,
        Diff.RemovedLines > 0 ? $"−{Diff.RemovedLines}" : null,
    }.OfType<string>());

    /// <summary>Stops at the last change rather than wrapping; false when it's already there.</summary>
    public bool NextChange() => ShowChange(Diff.ChangeBlocks.FirstOrDefault(s => s > CurrentDiffRow, -1));

    /// <summary>Stops at the first change rather than wrapping; false when it's already there.</summary>
    public bool PreviousChange() => ShowChange(Diff.ChangeBlocks.LastOrDefault(s => s < CurrentDiffRow, -1));

    /// <summary>Puts the first change in view; false when there are none.</summary>
    public bool FirstChange() => ShowChange(Diff.ChangeBlocks.FirstOrDefault(-1));

    /// <summary>Puts the last change in view; false when there are none.</summary>
    public bool LastChange() => ShowChange(Diff.ChangeBlocks.LastOrDefault(-1));

    /// <summary>
    /// Replaces the current change's buffer lines with the left side's, as one undo step, and recomputes
    /// the diff; the number of lines it touched, or null when this diff has nothing to revert (#245).
    /// A freshly opened diff sits above the first change, so from there it takes that one.
    /// </summary>
    public int? RevertChange()
    {
        if (Source is not { } source) return null;
        if (CurrentChange == 0 && !FirstChange()) return null;

        var block = Diff.Block(Diff.ChangeBlocks[CurrentChange - 1]);
        source.ReplaceLines(block.RightStart, block.RightCount, [.. _left.Skip(block.LeftStart).Take(block.LeftCount)]);
        Refresh();
        _reverted = (block.RightStart, block.RightStart + block.LeftCount);
        return Math.Max(block.LeftCount, block.RightCount);
    }

    /// <summary>The changes a revert would work through, the diff as it was last computed.</summary>
    public int ChangeCount => Diff.ChangeBlocks.Count;

    /// <summary>
    /// Puts the whole of the left side back into the buffer as one undo step, and recomputes the diff;
    /// the number of changes it threw away, or null when this diff has nothing to revert (#248).
    /// </summary>
    public int? RevertAll()
    {
        if (Source is not { } source) return null;
        if (ChangeCount == 0) return null;

        var changes = ChangeCount;
        source.ReplaceLines(0, source.Lines.Count, _left);
        Refresh();
        return changes;
    }

    /// <summary>Scrolls both sides sideways by one column, or by a quarter of the narrower side.</summary>
    public bool ScrollSideways(int direction, bool page = false) =>
        ScrollSidewaysTo(_column + direction * (page ? LargeSidewaysStep : 1));

    private bool ShowChange(int start)
    {
        if (start < 0) return false;
        _reverted = null;
        // Change blocks count rows of the diff; comment rows (#186, #188) sit between them and are stepped over.
        _current = Math.Max(0, _rows.FindIndex(r => r.Diff == start && !r.IsComment));
        ScrollTo(_current - Reveal.Margin);
        return true;
    }

    private int PageHeight => Math.Max(1, Viewport.Height - HeaderHeight - 1);

    private int HeaderHeight => _header?.Height is DimAbsolute { Size: var height } ? height : 0;

    /// <summary>Docks <paramref name="header"/> (the find bar, #413) above both sides; null removes it without disposing it.</summary>
    public void SetHeader(View? header)
    {
        if (ReferenceEquals(_header, header)) return;
        if (_header is not null) Remove(_header);
        _header = header;
        if (header is not null)
        {
            header.X = 0;
            header.Y = 0;
            header.Width = Dim.Fill();
            Add(header);
        }
        SetNeedsLayout();
        MoveTo(_current);
    }

    /// <summary>Paints every match in the find highlight colour (#413). An empty set clears it.</summary>
    public void SetHighlights(IEnumerable<DiffMatch> matches)
    {
        _found = matches
            .GroupBy(m => (m.Row, m.Side))
            .ToDictionary(g => g.Key, g => g.Select(m => new TextRange(m.Column, m.Length)).ToArray());
        if (_currentMatch is { } current && !_found.ContainsKey((current.Row, current.Side))) _currentMatch = null;
        SetNeedsDraw();
    }

    /// <summary>Draws <paramref name="match"/> as selected, moves the current row to it, and scrolls it into view (#413).</summary>
    public void ShowMatch(DiffMatch match)
    {
        _currentMatch = match;
        _reverted = null;
        MoveTo(Math.Max(0, _rows.FindIndex(r => r.Diff == match.Row && !r.IsComment)));

        var line = (match.Side == DiffSide.Left ? Diff.Rows[match.Row].Left : Diff.Rows[match.Row].Right) is { } l
            ? (match.Side == DiffSide.Left ? _left : _right)[l]
            : string.Empty;
        var start = Columns(line[..Math.Min(match.Column, line.Length)]);
        var end = Columns(line[..Math.Min(match.Column + match.Length, line.Length)]);
        var (leftWidth, rightWidth) = SideWidths();
        var width = Math.Max(1, (match.Side == DiffSide.Left ? leftWidth : rightWidth) - Digits - 2);
        if (start < _column) ScrollSidewaysTo(start);
        else if (end > _column + width) ScrollSidewaysTo(Math.Min(start, end - width));
    }

    public void ClearCurrentMatch()
    {
        _currentMatch = null;
        SetNeedsDraw();
    }

    internal DiffMatch? CurrentMatch => _currentMatch;

    /// <summary>Re-read both sides and recompute the diff.</summary>
    public void Refresh()
    {
        _reverted = null;
        _left = _readLeft();
        _right = Source is { } source ? [.. source.SnapshotLines] : _rightRevision ?? [];
        Diff = AlignedDiff.Compute(_left, _right, MaxEdits);
        WordChanges = ComputeWordChanges();
        _widest = null;
        var grammar = Source?.Grammar ?? _ownGrammar;
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
        MoveTo(_current);
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    private Dictionary<int, (TextRange[] Left, TextRange[] Right)> ComputeWordChanges()
    {
        var changes = new Dictionary<int, (TextRange[] Left, TextRange[] Right)>();
        for (var i = 0; i < Diff.Rows.Count; i++)
        {
            if (Diff.Rows[i] is not { Kind: DiffRowKind.Modified, Left: { } left, Right: { } right }) continue;
            var words = WordDiff.Changes(_left[left], _right[right]);
            if (words.Left.Length > 0 || words.Right.Length > 0) changes[i] = words;
        }
        return changes;
    }

    internal void UpdateTitle()
    {
        Title = IsDeleted ? $"{File.Name} ↔ {LeftLabel} (deleted)"
            : RightLabel is { } right ? $"{File.Name} {LeftLabel} ↔ {right}"
            : $"{File.Name} ↔ {LeftLabel}";
        PaneTabs.ShowTitle(this);
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

        var draftsByRow = new Dictionary<int, List<DraftComment>>();
        foreach (var draft in _drafts)
        {
            if (!rightRows.TryGetValue(draft.Line - 1, out var row)) continue;
            if (!draftsByRow.TryGetValue(row, out var drafts)) draftsByRow[row] = drafts = [];
            drafts.Add(draft);
        }

        _rows = [];
        _placed = [];
        _placedDrafts = [];
        for (var i = 0; i < Diff.Rows.Count; i++)
        {
            _rows.Add(new Row(i, null, null, default));
            if (byRow.TryGetValue(i, out var threads))
                foreach (var thread in threads)
                {
                    _placed.Add(thread);
                    foreach (var text in ReviewThreadRows.For(thread, _expanded.Contains(thread)))
                        _rows.Add(new Row(i, thread, null, text));
                }
            if (!draftsByRow.TryGetValue(i, out var onRow)) continue;
            foreach (var draft in onRow)
            {
                _placedDrafts.Add(draft);
                _rows.Add(new Row(i, null, draft, ReviewThreadRows.ForDraft(draft)));
            }
        }
        SyncScrollBar();
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
        SyncScrollBar();
        SetNeedsDraw();
        return true;
    }

    private void SyncScrollBar()
    {
        _syncingScrollBar = true;
        try
        {
            _scrollBar.VisibleContentSize = PageHeight;
            _scrollBar.ScrollableContentSize = _rows.Count;
            _scrollBar.Value = _top;
            _column = Math.Clamp(_column, 0, MaxColumn);
            _sidewaysBar.VisibleContentSize = Math.Max(1, Viewport.Width);
            _sidewaysBar.ScrollableContentSize = _sidewaysBar.VisibleContentSize + ToSidewaysBar(MaxColumn);
            _sidewaysBar.Increment = Math.Max(1, ToSidewaysBar(1));
            _sidewaysBar.Value = ToSidewaysBar(_column);
        }
        finally { _syncingScrollBar = false; }
    }

    internal ScrollBar ScrollBar => _scrollBar;

    internal ScrollBar SidewaysBar => _sidewaysBar;

    private bool ScrollSidewaysTo(int column)
    {
        _column = Math.Clamp(column, 0, MaxColumn);
        SyncScrollBar();
        SetNeedsDraw();
        return true;
    }

    private int Widest => _widest ??= _left.Concat(_right).Select(line => Columns(line)).DefaultIfEmpty().Max();

    private int MaxColumn => Math.Max(0, Widest - NarrowerTextWidth);

    // TG sizes a slider as if each bar cell were a column, but this bar spans both panes: its range is in bar cells.
    private double SidewaysBarScale => (double)Math.Max(1, Viewport.Width) / Math.Max(1, NarrowerTextWidth);

    private int ToSidewaysBar(int column) => (int)Math.Round(column * SidewaysBarScale);

    private int FromSidewaysBar(int value) => (int)Math.Round(value / SidewaysBarScale);

    private int Digits => Math.Max(MinDigits, Math.Max(_left.Count, _right.Count).ToString().Length);

    private (int Left, int Right) SideWidths()
    {
        var left = Math.Max(0, Viewport.Width - 1) / 2;
        return (left, Math.Max(0, Viewport.Width - left - 1));
    }

    private int NarrowerTextWidth => Math.Max(0, SideWidths().Left - Digits - 2);

    private const int PageOverlap = 2;

    internal int LargeSidewaysStep => Math.Max(1, NarrowerTextWidth - PageOverlap);

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = GetAttributeForRole(VisualRole.Editable);
        var removed = normal with { Background = ThemeColor("diffEditor.removedLineBackground") ?? DefaultRemoved };
        var inserted = normal with { Background = ThemeColor("diffEditor.insertedLineBackground") ?? DefaultInserted };
        var comment = normal with { Background = ThemeColor("editorCommentsWidget.rangeBackground") ?? DefaultComment };
        var removedText = ThemeColor("diffEditor.removedTextBackground") ?? DefaultRemovedText;
        var insertedText = ThemeColor("diffEditor.insertedTextBackground") ?? DefaultInsertedText;
        var (leftWidth, rightWidth) = SideWidths();
        var rightX = leftWidth + 1;
        var digits = Digits;

        var current = GetAttributeForRole(VisualRole.Focus);
        var finds = new Found(GetAttributeForRole(VisualRole.Highlight), GetAttributeForRole(VisualRole.Active));
        var reverted = normal with { Background = current.Background };
        var header = normal with { Style = normal.Style | TextStyle.Bold };
        PrepareSyntax();
        var top = HeaderHeight;
        DrawText(0, top, leftWidth, " " + LeftLabel, header);
        DrawSeparator(leftWidth, top, normal);
        DrawText(rightX, top, rightWidth, " " + (RightLabel ?? (IsDeleted ? "deleted" : "working copy")), header);

        for (var y = top + 1; y < Viewport.Height; y++)
        {
            var index = _top + y - top - 1;
            if (index < _rows.Count && _rows[index] is { IsComment: true } commentRow)
            {
                var resolved = comment with { Style = comment.Style | TextStyle.Faint };
                DrawThread(y, commentRow.Text, index == _current ? current
                    : commentRow.Thread is { Resolved: true } ? resolved
                    : comment);
                continue;
            }
            DiffRow? row = index < _rows.Count ? Diff.Rows[_rows[index].Diff] : null;
            var changed = row?.Kind is DiffRowKind.Modified or DiffRowKind.LeftOnly or DiffRowKind.RightOnly;
            Attribute? marked = row is not null && index == _current ? current : null;
            var tinted = IsReverted(row) ? reverted : normal;
            var words = index < _rows.Count && WordChanges.TryGetValue(_rows[index].Diff, out var found) ? found : ([], []);
            var diffRow = index < _rows.Count ? _rows[index].Diff : -1;
            DrawSide(0, y, leftWidth, row?.Left, _left, LeftTokens, digits, changed ? '-' : ' ', changed ? removed : tinted, normal, marked, words.Left, removedText, finds.On(this, diffRow, DiffSide.Left));
            DrawSeparator(leftWidth, y, normal);
            DrawSide(rightX, y, rightWidth, row?.Right, _right, RightTokens, digits, changed ? '+' : ' ', changed ? inserted : tinted, normal, marked, words.Right, insertedText, finds.On(this, diffRow, DiffSide.Right));
        }
        return true;
    }

    private bool IsReverted(DiffRow? row) =>
        _reverted is { } range && row?.Right is { } right && right >= range.Start && right < range.End;

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

    /// <summary>How find matches are drawn (#413): every match like the editor's highlight, the current one like a selection.</summary>
    private readonly record struct Found(Attribute Highlight, Attribute Selected, TextRange[]? Ranges = null, TextRange? Current = null)
    {
        public Found On(DiffTab diff, int row, DiffSide side) => this with
        {
            Ranges = diff._found.GetValueOrDefault((row, side)),
            Current = diff._currentMatch is { } m && m.Row == row && m.Side == side ? new TextRange(m.Column, m.Length) : null,
        };

        public Attribute? At(int chars)
        {
            if (Current is { } c && chars >= c.Start && chars < c.End) return Selected;
            if (Ranges is null) return null;
            foreach (var range in Ranges)
                if (chars >= range.Start && chars < range.End) return Highlight;
            return null;
        }
    }

    private void DrawSide(int x, int y, int width, int? line, IReadOnlyList<string> lines, LineTokenCache? tokens, int digits, char marker, Attribute tint, Attribute normal, Attribute? current, TextRange[] words, Color wordBackground, Found found)
    {
        var prefix = line is { } i ? $"{(i + 1).ToString().PadLeft(digits)}{marker} " : new string(' ', digits + 2);
        var number = marker == ' ' ? tint with { Style = tint.Style | TextStyle.Faint } : tint;
        var used = Math.Min(width, prefix.Length);
        DrawText(x, y, used, prefix, current ?? (line is null ? normal : number));
        if (line is { } l) DrawText(x + used, y, width - used, lines[l], tint, tokens?.TokensFor(l), _column, words, wordBackground, found);
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

    // Pads to width; the first `skip` columns and whatever passes width are cut. Token and word offsets are UTF-16 chars, not cells.
    private void DrawText(int x, int y, int width, string text, Attribute attribute, int[]? tokens = null, int skip = 0, TextRange[]? words = null, Color wordBackground = default, Found found = default)
    {
        SetAttribute(attribute);
        var col = 0;
        var chars = 0;
        var token = -1;
        var word = 0;
        var inWord = false;
        Attribute? marking = null;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext() && col - skip < width)
        {
            var grapheme = elements.GetTextElement();
            var next = token;
            if (tokens is { Length: > 0 })
            {
                next = Math.Max(token, 0);
                while (next + 2 < tokens.Length && tokens[next + 2] <= chars)
                    next += 2;
            }
            while (words is not null && word < words.Length && words[word].End <= chars)
                word++;
            var nowInWord = words is not null && word < words.Length && words[word].Start <= chars;
            var nowMarking = found.At(chars);
            if (next != token || nowInWord != inWord || nowMarking != marking)
            {
                var background = nowInWord ? attribute with { Background = wordBackground } : attribute;
                SetAttribute(nowMarking ?? (next < 0 ? background : _palette.Apply(background, tokens![next + 1])));
                token = next;
                inWord = nowInWord;
                marking = nowMarking;
            }
            chars += grapheme.Length;
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

    // The header (find bar) belongs to its controller, which outlives this tab — don't take it down with us.
    protected override void Dispose(bool disposing)
    {
        if (disposing) SetHeader(null);
        base.Dispose(disposing);
    }

    private Color? ThemeColor(string key) =>
        _syntax?.EditorColors.TryGetValue(key, out var hex) == true && Color.TryParse(hex, out Color? color) ? color : null;
}
