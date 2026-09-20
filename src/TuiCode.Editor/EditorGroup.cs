using TuiCode.Abstractions;
using TuiCode.Syntax;

namespace TuiCode.Editor;

public sealed class EditorGroup : Tabs
{
    private readonly Dictionary<string, EditorTab> _byPath = new(StringComparer.Ordinal);
    private readonly List<DiffTab> _diffs = [];
    private readonly SyntaxHighlighter? _syntax;

    public event EventHandler<IFileInfo>? FileSaved;
    public event EventHandler<EditorTab?>? ActiveTabChanged;

    /// <summary>Raised when any tab's grammar changes, e.g. from the grammar picker or new associations.</summary>
    public event EventHandler<EditorTab>? GrammarChanged;

    /// <summary>Raised when the cursor moves in any tab, tagged with the owning file (#35).</summary>
    public event EventHandler<(IFileInfo File, int Row, int Column)>? CursorMoved;

    /// <summary>The active editor tab; null while a diff tab is active, so editor commands leave it alone.</summary>
    public EditorTab? ActiveTab => Value as EditorTab;

    public DiffTab? ActiveDiffTab => Value as DiffTab;

    public SyntaxHighlighter? Syntax => _syntax;

    public IReadOnlyList<EditorTab> Tabs => _byPath.Values.ToArray();

    public IReadOnlyList<DiffTab> DiffTabs => _diffs.ToArray();

    /// <summary>Whether tabs show the line-number gutter (#23); applies to open and future tabs alike.</summary>
    public bool GutterVisible
    {
        get;
        set
        {
            field = value;
            foreach (var tab in _byPath.Values) tab.GutterVisible = value;
        }
    } = true;

    /// <summary>Whether selections sweep a rectangle (#114); applies to open and future tabs alike.</summary>
    public bool ColumnSelect
    {
        get;
        set
        {
            field = value;
            foreach (var tab in _byPath.Values) tab.ColumnSelect = value;
        }
    }

    /// <summary>Indentation and line endings (#14); applies to open and future tabs alike.</summary>
    public EditorSettings Settings
    {
        get;
        set
        {
            field = value;
            foreach (var tab in _byPath.Values) tab.Settings = value;
            foreach (var diff in _diffs) diff.Settings = value;
        }
    } = EditorSettings.Default;

    public EditorGroup(SyntaxHighlighter? syntax = null)
    {
        _syntax = syntax;
        ValueChanged += (_, _) =>
        {
            ActiveDiffTab?.Refresh();
            ActiveTabChanged?.Invoke(this, ActiveTab);
        };
    }

    public EditorTab OpenOrFocus(IFileInfo file) =>
        Focus(file.FullName) ?? Track(new EditorTab(file, _syntax));

    /// <summary>Opens or focuses a read-only document that isn't on disk (#185), like a PR's Overview.</summary>
    public EditorTab OpenOrFocusDocument(IFileInfo file, string content, SyntaxLanguage? grammar) =>
        Focus(file.FullName) ?? Track(new EditorTab(file, content, grammar, _syntax));

    /// <summary>Focuses the open tab for <paramref name="path"/>; null when nothing is open for it.</summary>
    public EditorTab? Focus(string path)
    {
        if (!_byPath.TryGetValue(path, out var tab)) return null;
        Value = tab;
        return tab;
    }

    private EditorTab Track(EditorTab tab)
    {
        tab.GutterVisible = GutterVisible;
        tab.ColumnSelect = ColumnSelect;
        tab.Settings = Settings;
        tab.Saved += (_, _) => FileSaved?.Invoke(this, tab.File);
        tab.CursorMoved += (_, p) => CursorMoved?.Invoke(this, (tab.File, p.Row, p.Column));
        tab.GrammarChanged += (_, _) => GrammarChanged?.Invoke(this, tab);
        // Tabs selects the first tab it's given during Add, so register it first for ActiveTabChanged listeners to see.
        _byPath[tab.File.FullName] = tab;
        Add(tab);
        Value = tab;
        return tab;
    }

    /// <summary>Returns null, opening nothing, when the buffer matches the file on disk.</summary>
    public DiffTab? CompareToSaved(EditorTab source) => Compare(source, "saved", () => DiffTab.ReadLines(source.File));

    /// <summary>Opens or focuses the diff of <paramref name="readLeft"/> against the buffer; null, opening nothing, when they match.</summary>
    public DiffTab? Compare(EditorTab source, string label, Func<IReadOnlyList<string>> readLeft, string? key = null)
    {
        if (readLeft().SequenceEqual(source.SnapshotLines, StringComparer.Ordinal)) return null;

        var tab = FindDiff(source, key ?? label);
        if (tab is null)
        {
            tab = new DiffTab(source, label, readLeft, _syntax, key);
            _diffs.Add(tab);
            Add(tab);
        }
        // Refreshes it, via ValueChanged, unless it's already showing.
        if (ReferenceEquals(Value, tab)) tab.Refresh();
        else Value = tab;
        return tab;
    }

    /// <summary>Opens or focuses the diff of a file deleted in this branch (#182); there's no editor tab and nothing on the right.</summary>
    public DiffTab CompareDeleted(IFileInfo file, string label, Func<IReadOnlyList<string>> readLeft, string key)
    {
        if (FocusDeletedDiff(file, key) is { } open) return open;

        var tab = new DiffTab(file, label, readLeft, _syntax, key) { Settings = Settings };
        _diffs.Add(tab);
        Add(tab);
        Value = tab;
        return tab;
    }

    /// <summary>Focuses an open deleted-file diff (#182); null when there is none.</summary>
    public DiffTab? FocusDeletedDiff(IFileInfo file, string key)
    {
        if (_diffs.FirstOrDefault(d => d.IsDeleted && d.LeftKey == key && d.File.FullName == file.FullName) is not { } tab) return null;
        if (ReferenceEquals(Value, tab)) tab.Refresh();
        else Value = tab;
        return tab;
    }

    public bool HasDiff(EditorTab source, string label) => FindDiff(source, label) is not null;

    /// <summary>Focuses an open diff of <paramref name="source"/> against <paramref name="label"/>; null when there is none.</summary>
    public DiffTab? FocusDiff(EditorTab source, string label)
    {
        if (FindDiff(source, label) is not { } tab) return null;
        if (ReferenceEquals(Value, tab)) tab.Refresh();
        else Value = tab;
        return tab;
    }

    private DiffTab? FindDiff(EditorTab source, string key) =>
        _diffs.FirstOrDefault(d => d.Source == source && d.LeftKey == key);

    public void CloseActive()
    {
        if (Value is { } tab) Close(tab);
    }

    /// <summary>Closes a diff tab, leaving the file's own tab open.</summary>
    public void CloseDiff(DiffTab diff) => Close(diff);

    public IEnumerable<EditorTab> TabsUnder(string path) =>
        _byPath.Values.Where(t => FilePaths.IsSameOrUnder(t.File.FullName, path));

    /// <summary>Discards unsaved changes.</summary>
    public void CloseUnder(string path)
    {
        foreach (var tab in TabsUnder(path).ToList())
            Close(tab);
    }

    public void Relocate(string from, string to)
    {
        var tabs = _byPath.Values.ToList();
        foreach (var tab in tabs.Where(t => FilePaths.IsSameOrUnder(t.File.FullName, from)))
            tab.Relocate(tab.File.FileSystem.FileInfo.New(FilePaths.Rebase(tab.File.FullName, from, to)));
        foreach (var diff in _diffs)
            diff.UpdateTitle();

        _byPath.Clear();
        foreach (var tab in tabs)
            _byPath[tab.File.FullName] = tab;
    }

    private void Close(View tab)
    {
        List<View> closing = tab is EditorTab editor ? [.. _diffs.Where(d => d.Source == editor), tab] : [tab];
        var strip = TabCollection.ToList();
        var before = strip.Take(strip.IndexOf(tab)).Count(t => !closing.Contains(t));
        var wasActive = Value is not null && closing.Contains(Value);

        foreach (var closed in closing)
        {
            if (closed is EditorTab e) _byPath.Remove(e.File.FullName);
            if (closed is DiffTab d) _diffs.Remove(d);
            Remove(closed);
            closed.Dispose();
        }

        var remaining = strip.Where(t => !closing.Contains(t)).ToList();
        if (remaining.Count == 0)
        {
            ClearValue();
            return;
        }
        if (!wasActive) return;

        Value = remaining[Math.Min(before, remaining.Count - 1)];
    }

    /// <summary>Close every open tab — used when switching workspace folders.</summary>
    public void CloseAll()
    {
        foreach (var tab in TabCollection.ToList())
        {
            Remove(tab);
            tab.Dispose();
        }
        _byPath.Clear();
        _diffs.Clear();
        ClearValue();
    }

    // Removing the selected tab when it's the last one makes TG's Tabs null its value silently — no
    // ValueChanged — so raise ActiveTabChanged ourselves; listeners (find bar, history) must see "no editor".
    private void ClearValue()
    {
        if (Value is null) ActiveTabChanged?.Invoke(this, null);
        else Value = null;
    }

    public void SaveActive() => ActiveTab?.Save();

    /// <summary>Re-pick every tab's grammar after the associations change; tabs with a chosen grammar keep it.</summary>
    public void InferGrammars()
    {
        foreach (var tab in _byPath.Values)
            tab.InferGrammar();
    }

    public void NextTab() => CycleTab(forward: true);
    public void PreviousTab() => CycleTab(forward: false);

    public bool FocusByIndex(int index)
    {
        var tabs = TabCollection.ToList();
        if (index < 0 || index >= tabs.Count) return false;
        Value = tabs[index];
        return true;
    }

    public bool FocusActive() => Value switch
    {
        EditorTab tab => tab.FocusContent(),
        DiffTab diff => diff.SetFocus(),
        _ => false,
    };

    private void CycleTab(bool forward)
    {
        var tabs = TabCollection.ToList();
        if (tabs.Count < 2 || Value is null) return;
        var i = tabs.IndexOf(Value);
        var n = tabs.Count;
        var next = forward ? (i + 1) % n : (i - 1 + n) % n;
        Value = tabs[next];
    }
}
