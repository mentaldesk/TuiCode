using TuiCode.Abstractions;
using TuiCode.Syntax;

namespace TuiCode.Editor;

/// <summary>
/// The editor area's two groups (#460): the first always shows, the second only while it holds a tab. A file is
/// open in at most one of them, and the commands that act on "the active tab" act on <see cref="Focused"/>'s.
/// </summary>
public sealed class EditorGroups
{
    private readonly EditorGroup[] _all;

    public event EventHandler<IFileInfo>? FileSaved;
    public event EventHandler<CopyOutcome>? Copied;
    public event EventHandler? TabsChanged;
    public event EventHandler<EditorTab>? BaselineReset;
    public event EventHandler<EditorTab>? GrammarChanged;
    public event EventHandler<(IFileInfo File, int Row, int Column)>? CursorMoved;

    /// <summary>Raised when either group's active tab changes or the focus moves to the other group; carries the focused group's.</summary>
    public event EventHandler<EditorTab?>? ActiveTabChanged;

    /// <summary>Raised when the second group appears or goes.</summary>
    public event EventHandler? SplitChanged;

    public EditorGroups(EditorGroup first, EditorGroup second)
    {
        _all = [first, second];
        Focused = first;
        foreach (var group in _all)
        {
            group.FileSaved += (_, file) => FileSaved?.Invoke(this, file);
            group.Copied += (_, outcome) => Copied?.Invoke(this, outcome);
            group.BaselineReset += (_, tab) => BaselineReset?.Invoke(this, tab);
            group.GrammarChanged += (_, tab) => GrammarChanged?.Invoke(this, tab);
            group.CursorMoved += (_, e) => CursorMoved?.Invoke(this, e);
            group.ActiveTabChanged += (_, _) =>
            {
                // A tab opened into the second group lands after its TabsChanged, so the split shows up here.
                if (!_batching) Settle();
                ActiveTabChanged?.Invoke(this, ActiveTab);
            };
            group.TabsChanged += (_, _) => OnTabsChanged();
            group.EditorClosed += (sender, tab) => CloseDiffsOf(tab, except: (EditorGroup)sender!);
        }
    }

    public EditorGroup First => _all[0];
    public EditorGroup Second => _all[1];
    public IReadOnlyList<EditorGroup> All => _all;

    public EditorGroup Focused
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            ActiveTabChanged?.Invoke(this, ActiveTab);
        }
    }

    public EditorGroup Other => ReferenceEquals(Focused, First) ? Second : First;

    public bool IsSplit { get; private set; }

    public EditorTab? ActiveTab => Focused.ActiveTab;

    public IReadOnlyList<EditorTab> Tabs => [.. _all.SelectMany(g => g.Tabs)];

    public IReadOnlyList<DiffTab> DiffTabs => [.. _all.SelectMany(g => g.DiffTabs)];

    public SyntaxHighlighter? Syntax => First.Syntax;

    public EditorGroup? Holding(string path) => _all.FirstOrDefault(g => g.Tabs.Any(t => t.File.FullName == path));

    public EditorGroup? Holding(View tab) => _all.FirstOrDefault(g => g.TabCollection.Contains(tab));

    /// <summary>Focuses the tab already open for <paramref name="file"/>, in whichever group has it, or opens it in the focused group.</summary>
    public EditorTab Open(IFileInfo file)
    {
        if (Holding(file.FullName) is { } group)
        {
            Focused = group;
            return group.Focus(file.FullName)!;
        }
        return Focused.OpenOrFocus(file);
    }

    /// <summary>Opens the base version of a deleted file (#247) unsaved, or focuses its tab wherever it is.</summary>
    public EditorTab Restore(IFileInfo file, IReadOnlyList<string> lines)
    {
        if (Holding(file.FullName) is { } group) Focused = group;
        return Focused.Restore(file, lines);
    }

    /// <summary>Moves the focused group's active tab to the other group, splitting if need be; false when it's the only tab and there's no split.</summary>
    public bool MoveActiveToOther()
    {
        if (Focused.Value is not { } tab) return false;
        if (!IsSplit && Focused.TabCollection.Count() < 2) return false;
        var from = Focused;
        Batch(() => from.MoveTo(Other, tab));
        Focused = Holding(tab) ?? First;
        return true;
    }

    /// <summary>Moves focus to the other group; false when there's no split.</summary>
    public bool FocusOther()
    {
        if (!IsSplit) return false;
        Focused = Other;
        return true;
    }

    /// <summary>Moves every tab into the first group, keeping the focused tab active.</summary>
    public void Join()
    {
        var active = Focused.Value;
        Batch(() =>
        {
            foreach (var tab in Second.TabCollection.ToList())
                Second.MoveTo(First, tab);
        });
        if (active is not null) First.Value = active;
        Focused = First;
    }

    public bool GutterVisible
    {
        get => First.GutterVisible;
        set { foreach (var group in _all) group.GutterVisible = value; }
    }

    public bool ColumnSelect
    {
        get => First.ColumnSelect;
        set { foreach (var group in _all) group.ColumnSelect = value; }
    }

    public FileIconStyle IconStyle
    {
        get => First.IconStyle;
        set { foreach (var group in _all) group.IconStyle = value; }
    }

    public EditorSettings Settings
    {
        get => First.Settings;
        set { foreach (var group in _all) group.Settings = value; }
    }

    public IEnumerable<EditorTab> TabsUnder(string path) => _all.SelectMany(g => g.TabsUnder(path)).ToList();

    public void CloseUnder(string path)
    {
        foreach (var group in _all) group.CloseUnder(path);
    }

    public void Relocate(string from, string to)
    {
        foreach (var group in _all) group.Relocate(from, to);
    }

    public void CloseAll()
    {
        Second.CloseAll();
        First.CloseAll();
        Focused = First;
    }

    public void InferGrammars()
    {
        foreach (var group in _all) group.InferGrammars();
    }

    private void CloseDiffsOf(EditorTab source, EditorGroup except)
    {
        foreach (var group in _all.Where(g => !ReferenceEquals(g, except)))
            foreach (var diff in group.DiffTabs.Where(d => d.Source == source))
                group.CloseDiff(diff);
    }

    private bool _batching;

    private void Batch(Action moves)
    {
        _batching = true;
        try
        {
            moves();
        }
        finally
        {
            _batching = false;
        }
        Settle();
    }

    private void OnTabsChanged()
    {
        TabsChanged?.Invoke(this, EventArgs.Empty);
        if (!_batching) Settle();
    }

    // A group left with no tabs closes, and the other takes the whole width.
    private void Settle()
    {
        if (!First.TabCollection.Any() && Second.TabCollection.Any())
        {
            var active = Second.Value;
            Batch(() =>
            {
                foreach (var tab in Second.TabCollection.ToList())
                    Second.MoveTo(First, tab);
            });
            if (active is not null) First.Value = active;
            return;
        }
        var split = Second.TabCollection.Any();
        if (!split) Focused = First;
        if (split == IsSplit) return;
        IsSplit = split;
        SplitChanged?.Invoke(this, EventArgs.Empty);
    }
}
