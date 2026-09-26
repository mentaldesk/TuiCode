using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;

namespace TuiCode.Workbench.Files;

/// <summary>
/// What a change on disk does to a tab. A clean tab takes the new text up by itself (#269); a dirty one keeps
/// its edits and gets the marker instead (#268). Either way it's verified by re-reading the file and comparing
/// it with the snapshot the tab took (#267), so our own saves and a <c>git checkout</c> that rewrites a file
/// byte for byte do nothing at all. No modal, no focus steal, no beep: a marker, a colour and one status line.
/// </summary>
internal sealed class DiskChanges : IDisposable
{
    private readonly EditorGroup _group;
    private readonly FileExplorerView _explorer;
    private readonly Action<string> _announce;
    private readonly DiskWatcher _watcher;

    public DiskChanges(EditorGroup group, FileExplorerView explorer, Action<string> announce, DiskWatcher watcher)
    {
        _group = group;
        _explorer = explorer;
        _announce = announce;
        _watcher = watcher;

        _watcher.Changed += OnChanged;
        _group.TabsChanged += (_, _) => Follow();
        // A save clears the tab's own marker; the explorer needs telling.
        _group.FileSaved += (_, _) => ShowMarks();
        _group.ActiveTabChanged += OnActiveTabChanged;
        Follow();
    }

    private void Follow()
    {
        _watcher.Follow(_group.Tabs.Select(tab => tab.File.FullName));
        ShowMarks();
    }

    private void OnChanged(object? sender, IReadOnlyList<string> paths)
    {
        var moved = false;
        foreach (var path in paths)
        {
            if (_group.Tabs.FirstOrDefault(t => string.Equals(t.File.FullName, path, StringComparison.Ordinal)) is { } tab)
                moved |= Act(tab);
        }
        if (moved) ShowMarks();
    }

    // Graceful degradation, not a second mechanism: a directory whose watcher failed hears nothing, so its
    // tabs ask the same question when you switch to them. A watched directory has already told us.
    private void OnActiveTabChanged(object? sender, EditorTab? tab)
    {
        if (tab is null || _watcher.IsWatching(tab.File.FullName)) return;
        if (Act(tab)) ShowMarks();
    }

    // Whether anything on screen changed.
    private bool Act(EditorTab tab)
    {
        if (tab.IsDirty)
        {
            var changed = tab.ChangedOnDisk;
            if (changed == tab.ChangedOnDiskMarked) return false;
            tab.MarkChangedOnDisk(changed);
            // Once per tab, and only on the way in: it's told you, and you can keep typing.
            if (changed) _announce($"{WarningMark.For(_group.IconStyle)} {tab.File.Name} changed on disk");
            return true;
        }

        if (!tab.Reload()) return false;
        // A background tab reloads with nothing on screen at all.
        if (ReferenceEquals(tab, _group.ActiveTab)) _announce($"⟳ Reloaded {tab.File.Name} — changed on disk");
        return true;
    }

    /// <summary>
    /// Take up what's on disk because the user asked (#270) — the same reload as above, edits and all, with
    /// the explorer told that the marker has gone.
    /// </summary>
    public bool Reload(EditorTab tab)
    {
        if (!tab.Reload()) return false;
        ShowMarks();
        return true;
    }

    private void ShowMarks() =>
        _explorer.ShowChangedOnDisk(_group.Tabs.Where(t => t.ChangedOnDiskMarked).Select(t => t.File.FullName));

    public void Dispose()
    {
        _watcher.Changed -= OnChanged;
        _group.ActiveTabChanged -= OnActiveTabChanged;
        _watcher.Dispose();
    }
}
