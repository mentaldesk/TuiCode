using TuiCode.Abstractions;
using TuiCode.Syntax;

namespace TuiCode.Editor;

public sealed class EditorGroup : Tabs
{
    private readonly Dictionary<string, EditorTab> _byPath = new(StringComparer.Ordinal);
    private readonly SyntaxHighlighter? _syntax;

    public event EventHandler<IFileInfo>? FileSaved;
    public event EventHandler<EditorTab?>? ActiveTabChanged;

    /// <summary>Raised when any tab's grammar changes, e.g. from the grammar picker or new associations.</summary>
    public event EventHandler<EditorTab>? GrammarChanged;

    /// <summary>Raised when the cursor moves in any tab, tagged with the owning file (#35).</summary>
    public event EventHandler<(IFileInfo File, int Row, int Column)>? CursorMoved;

    public EditorTab? ActiveTab => Value as EditorTab;

    public SyntaxHighlighter? Syntax => _syntax;

    public IReadOnlyList<EditorTab> Tabs => _byPath.Values.ToArray();

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

    public EditorGroup(SyntaxHighlighter? syntax = null)
    {
        _syntax = syntax;
        ValueChanged += (_, _) => ActiveTabChanged?.Invoke(this, ActiveTab);
    }

    public EditorTab OpenOrFocus(IFileInfo file)
    {
        if (_byPath.TryGetValue(file.FullName, out var existing))
        {
            Value = existing;
            return existing;
        }

        var tab = new EditorTab(file, _syntax) { GutterVisible = GutterVisible, ColumnSelect = ColumnSelect };
        tab.Saved += (_, _) => FileSaved?.Invoke(this, tab.File);
        tab.CursorMoved += (_, p) => CursorMoved?.Invoke(this, (tab.File, p.Row, p.Column));
        tab.GrammarChanged += (_, _) => GrammarChanged?.Invoke(this, tab);
        // Tabs selects the first tab it's given during Add, so register it first for ActiveTabChanged listeners to see.
        _byPath[file.FullName] = tab;
        Add(tab);
        Value = tab;
        return tab;
    }

    public void CloseActive()
    {
        if (ActiveTab is { } tab) Close(tab);
    }

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

        _byPath.Clear();
        foreach (var tab in tabs)
            _byPath[tab.File.FullName] = tab;
    }

    private void Close(EditorTab tab)
    {
        var tabs = _byPath.Values.ToList();
        var index = tabs.IndexOf(tab);
        var wasActive = ReferenceEquals(tab, ActiveTab);

        _byPath.Remove(tab.File.FullName);
        Remove(tab);
        tab.Dispose();

        if (_byPath.Count == 0)
        {
            ClearValue();
            return;
        }
        if (!wasActive) return;

        var nextIndex = Math.Min(index, _byPath.Count - 1);
        Value = tabs.Where(t => t != tab).ElementAt(nextIndex);
    }

    /// <summary>Close every open tab — used when switching workspace folders.</summary>
    public void CloseAll()
    {
        foreach (var tab in _byPath.Values.ToList())
        {
            Remove(tab);
            tab.Dispose();
        }
        _byPath.Clear();
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
        if (index < 0 || index >= _byPath.Count) return false;
        Value = _byPath.Values.ElementAt(index);
        return true;
    }

    private void CycleTab(bool forward)
    {
        if (_byPath.Count < 2 || ActiveTab is null) return;
        var tabs = _byPath.Values.ToList();
        var i = tabs.IndexOf(ActiveTab);
        var n = tabs.Count;
        var next = forward ? (i + 1) % n : (i - 1 + n) % n;
        Value = tabs[next];
    }
}
