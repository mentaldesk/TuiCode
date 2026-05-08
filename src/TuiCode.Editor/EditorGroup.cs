namespace TuiCode.Editor;

public sealed class EditorGroup : Tabs
{
    private readonly Dictionary<string, EditorTab> _byPath = new(StringComparer.Ordinal);

    public event EventHandler<IFileInfo>? FileSaved;
    public event EventHandler<EditorTab?>? ActiveTabChanged;

    public EditorTab? ActiveTab => Value as EditorTab;

    public IReadOnlyList<EditorTab> Tabs => _byPath.Values.ToArray();

    public EditorGroup()
    {
        ValueChanged += (_, _) => ActiveTabChanged?.Invoke(this, ActiveTab);
    }

    public EditorTab OpenOrFocus(IFileInfo file)
    {
        if (_byPath.TryGetValue(file.FullName, out var existing))
        {
            Value = existing;
            return existing;
        }

        var tab = new EditorTab(file);
        tab.Saved += (_, _) => FileSaved?.Invoke(this, tab.File);
        Add(tab);
        _byPath[file.FullName] = tab;
        Value = tab;
        return tab;
    }

    public void CloseActive()
    {
        if (ActiveTab is not { } tab) return;

        var tabs = _byPath.Values.ToList();
        var index = tabs.IndexOf(tab);

        _byPath.Remove(tab.File.FullName);
        Remove(tab);
        tab.Dispose();

        if (_byPath.Count == 0)
        {
            Value = null;
            return;
        }

        var nextIndex = Math.Min(index, _byPath.Count - 1);
        Value = tabs.Where(t => t != tab).ElementAt(nextIndex);
    }

    public void SaveActive() => ActiveTab?.Save();

    public void NextTab() => CycleTab(forward: true);
    public void PreviousTab() => CycleTab(forward: false);

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
