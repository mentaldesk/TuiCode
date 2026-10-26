using Terminal.Gui.Editor.Document.Folding;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

// Folding (#477); see "Folding" in AGENTS.md.
internal sealed partial class CodeEditor
{
    private FoldingManager? _folds;
    private bool _foldsStale;
    private bool[]? _hidden;
    private readonly Dictionary<int, FoldingSection> _foldedAt = [];
    private FoldRanges? _foldRules;

    internal FoldRanges FoldRules
    {
        get
        {
            var language = Syntax?.Language;
            if (_foldRules is null || !ReferenceEquals(_foldRulesFor.Language, language) || _foldRulesFor.TabWidth != TabWidth)
            {
                _foldRules = new FoldRanges(language, TabWidth);
                _foldRulesFor = (language, TabWidth);
            }
            return _foldRules;
        }
    }

    private (TuiCode.Syntax.SyntaxLanguage? Language, int TabWidth) _foldRulesFor;

    /// <summary>Raised after a fold or unfold, whatever asked for it.</summary>
    public event EventHandler? FoldsChanged;

    /// <summary>Lines a fold hides, or null while nothing is folded.</summary>
    private bool[]? HiddenLines
    {
        get
        {
            RefreshFolds();
            return _hidden;
        }
    }

    internal bool IsHidden(int line) => HiddenLines is { } hidden && line >= 0 && line < hidden.Length && hidden[line];

    internal bool IsFoldedAt(int line)
    {
        RefreshFolds();
        return _foldedAt.ContainsKey(line) && !IsHidden(line);
    }

    internal bool StartsBlock(int line) => FoldRules.StartsBlock(_lines, line);

    public bool ToggleFold()
    {
        var line = CurrentRow;
        if (IsFoldedAt(line)) return ChangeFolds(() => Unfold(line));
        var ranges = FoldRules.Find(_lines);
        FoldRange? target = null;
        foreach (var range in ranges)
        {
            if (range.Start == line) return Fold(range);
            if (range.Start < line && range.End >= line && (target is not { } inner || range.Start >= inner.Start))
                target = range;
        }
        return target is { } found && Fold(found);
    }

    /// <summary>Folds or unfolds the block starting on <paramref name="line"/>, as clicking its gutter marker does.</summary>
    internal bool ToggleFoldAt(int line)
    {
        if (IsFoldedAt(line)) return ChangeFolds(() => Unfold(line));
        foreach (var range in FoldRules.Find(_lines))
            if (range.Start == line) return Fold(range);
        return false;
    }

    public int FoldAll()
    {
        var caret = CurrentRow;
        List<FoldRange> folding = [];
        Stack<FoldRange> enclosing = [];
        foreach (var range in FoldRules.Find(_lines))
        {
            while (enclosing.Count > 0 && enclosing.Peek().End < range.End) enclosing.Pop();
            var outer = enclosing.Count > 0 ? enclosing.Peek() : (FoldRange?)null;
            enclosing.Push(range);
            if (!Contains(range, caret) && (outer is null || Contains(outer.Value, caret))) folding.Add(range);
        }
        if (folding.Count == 0) return 0;
        ChangeFolds(() =>
        {
            foreach (var range in folding) AddFold(range);
            return true;
        });
        return folding.Count;

        static bool Contains(FoldRange range, int line) => range.Start <= line && line <= range.End;
    }

    public int UnfoldAll()
    {
        if (_folds is not { } folds) return 0;
        var count = folds.AllFoldings.Count(f => f.IsFolded);
        if (count == 0) return 0;
        ChangeFolds(() =>
        {
            folds.Clear();
            return true;
        });
        return count;
    }

    /// <summary>Unfolds whatever hides a line from <paramref name="first"/> to <paramref name="last"/>, so a jump there shows it.</summary>
    public void RevealFolded(int first, int last)
    {
        if (HiddenLines is null || _folds is not { } folds) return;
        var hiding = folds.AllFoldings.Where(f => f.IsFolded && LinesOf(f) is var (start, end) && start < last && end >= first).ToList();
        if (hiding.Count == 0) return;
        ChangeFolds(() =>
        {
            foreach (var section in hiding) section.IsFolded = false;
            return true;
        });
    }

    /// <summary>What the folded row on <paramref name="line"/> shows after the line's own text, or null when it isn't one.</summary>
    internal string? FoldSummary(int line)
    {
        if (!IsFoldedAt(line)) return null;
        var (start, end) = LinesOf(_foldedAt[line]);
        return FoldRanges.Summary(_lines, start, end, _foldedAt[line].Tag is true);
    }

    private bool Fold(FoldRange range) => ChangeFolds(() => AddFold(range));

    private bool AddFold(FoldRange range)
    {
        var document = Document!;
        var start = document.GetLineByNumber(range.Start + 1).EndOffset;
        var end = document.GetLineByNumber(range.End + 1).EndOffset;
        if (end <= start) return false;
        var folds = _folds ??= NewFolds();
        foreach (var stale in folds.AllFoldings.Where(f => LinesOf(f).Start == range.Start).ToList())
            folds.RemoveFolding(stale);
        var section = folds.CreateFolding(start, end);
        section.Tag = range.ShowsEnd;
        section.IsFolded = true;
        return true;
    }

    private bool Unfold(int line)
    {
        foreach (var section in _folds!.AllFoldings.Where(f => f.IsFolded && LinesOf(f).Start == line).ToList())
            section.IsFolded = false;
        return true;
    }

    private FoldingManager NewFolds()
    {
        var folds = new FoldingManager(Document!);
        folds.FoldingChanged += (_, _) =>
        {
            _foldsStale = true;
            SetNeedsDraw();
        };
        return folds;
    }

    private bool ChangeFolds(Func<bool> change)
    {
        var topLine = TopLine;
        var changed = change();
        _foldsStale = true;
        if (!changed) return false;
        var mapped = _mapped;
        SyncRowMapping();
        if (_mapped && mapped)
        {
            RefreshWrap();
            ScrollToRow(_wrap.FirstRow(ShownLine(topLine)));
        }
        else if (!_mapped && (CurrentRow < Viewport.Y || CurrentRow >= Viewport.Y + Viewport.Height))
        {
            base.Viewport = Viewport with { Y = CurrentRow < Viewport.Y ? CurrentRow : CurrentRow - Viewport.Height + 1 };
        }
        LiftCaretsOutOfFolds();
        _followed = null;
        SetNeedsDraw();
        FoldsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void LiftCaretsOutOfFolds()
    {
        var carets = Carets;
        var lifted = carets.Select(Lift).ToArray();
        if (!carets.SequenceEqual(lifted)) SetCarets(lifted, reveal: false);

        Caret Lift(Caret caret)
        {
            if (!IsHidden(caret.Position.Y) && (caret.Anchor is not { } anchor || !IsHidden(anchor.Y))) return caret;
            var line = ShownLine(caret.Position.Y);
            return new Caret(new Point(Math.Min(caret.Position.X, LineLength(line)), line));
        }
    }

    private void RevealCaret()
    {
        if (!_visitingCarets && IsHidden(CurrentRow)) RevealFolded(CurrentRow, CurrentRow);
    }

    /// <summary>The line a row shows for <paramref name="line"/>: itself, or the folded row hiding it.</summary>
    private int ShownLine(int line)
    {
        while (line > 0 && IsHidden(line)) line--;
        return line;
    }

    private (int Start, int End) LinesOf(FoldingSection section)
    {
        var document = Document!;
        return (document.GetLineByOffset(section.StartOffset).LineNumber - 1,
            document.GetLineByOffset(Math.Min(section.EndOffset, document.TextLength)).LineNumber - 1);
    }

    private void ForgetFolds()
    {
        _folds?.Clear();
        _foldsStale = true;
    }

    private void RefreshFolds()
    {
        if (!_foldsStale) return;
        _foldsStale = false;
        _foldedAt.Clear();
        if (_folds is null)
        {
            _hidden = null;
            return;
        }
        bool[]? hidden = null;
        foreach (var section in _folds.AllFoldings)
        {
            if (!section.IsFolded) continue;
            var (start, end) = LinesOf(section);
            if (end <= start) continue;
            hidden ??= new bool[_lines.Count];
            for (var line = start + 1; line <= end && line < hidden.Length; line++)
                hidden[line] = true;
            if (!_foldedAt.TryGetValue(start, out var outer) || outer.EndOffset < section.EndOffset) _foldedAt[start] = section;
        }
        // The same array while nothing moved, so the row map isn't laid out again.
        if (hidden is null || _hidden is null || !hidden.AsSpan().SequenceEqual(_hidden)) _hidden = hidden;
    }
}
