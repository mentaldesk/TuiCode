using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;

namespace TuiCode.Workbench.Files;

/// <summary>
/// Turns a <see cref="DiskWatcher"/> event into the marker on a tab (#268). Every event is verified by
/// re-reading the file and comparing it with the snapshot the tab took (#267), so our own saves and a
/// <c>git checkout</c> that rewrites a file byte for byte mark nothing. No modal, no focus steal, no
/// beep: the marker, the warning colour and one status line are the whole of it.
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
            if (_group.Tabs.FirstOrDefault(t => string.Equals(t.File.FullName, path, StringComparison.Ordinal)) is not { } tab)
                continue;
            var changed = tab.ChangedOnDisk;
            if (changed == tab.ChangedOnDiskMarked) continue;
            tab.MarkChangedOnDisk(changed);
            // Once per tab, and only on the way in: it's told you, and you can keep typing.
            if (changed) _announce($"{WarningMark.For(_group.IconStyle)} {tab.File.Name} changed on disk");
            moved = true;
        }
        if (moved) ShowMarks();
    }

    private void ShowMarks() =>
        _explorer.ShowChangedOnDisk(_group.Tabs.Where(t => t.ChangedOnDiskMarked).Select(t => t.File.FullName));

    public void Dispose()
    {
        _watcher.Changed -= OnChanged;
        _watcher.Dispose();
    }
}
