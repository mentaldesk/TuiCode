using Microsoft.Extensions.Logging.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench.Files;

namespace TuiCode.Tests;

// What a watcher event does to a tab (#268): verified by reading, then the marker and one status line.
public class DiskChangesTests : IDisposable
{
    private readonly WatchableFileSystem _fs = new();
    private readonly EditorGroup _group = new();
    private readonly FileExplorerView _explorer = new();
    private readonly List<string> _said = [];
    private readonly List<Action> _flushes = [];
    private readonly DiskWatcher _watcher;
    private readonly DiskChanges _changes;

    public DiskChangesTests()
    {
        _fs.AddDirectory("/work");
        _watcher = new DiskWatcher(_fs, (_, flush) => _flushes.Add(flush), NullLogger.Instance);
        _changes = new DiskChanges(_group, _explorer, _said.Add, _watcher);
    }

    public void Dispose()
    {
        _changes.Dispose();
        _group.Dispose();
        _explorer.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_change_someone_else_made_marks_the_tab_and_says_so_once()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";

        Change(tab, "from the other branch\n");

        Assert.True(tab.ChangedOnDiskMarked);
        Assert.Equal("● a.txt ⚠", tab.Title);
        Assert.Equal(["⚠ a.txt changed on disk"], _said);

        Change(tab, "and again\n");
        Assert.Single(_said);
    }

    [Fact]
    public void A_checkout_that_rewrites_the_file_byte_for_byte_marks_nothing()
    {
        var tab = Open("/work/a.txt", "one\n");

        Change(tab, "one\n");

        Assert.False(tab.ChangedOnDiskMarked);
        Assert.Equal("a.txt", tab.Title);
        Assert.Empty(_said);
    }

    [Fact]
    public void Our_own_save_does_not_mark_the_tab_it_just_wrote()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";

        tab.Save();
        Raise(tab);
        Flush();

        Assert.False(tab.ChangedOnDiskMarked);
        Assert.Empty(_said);
    }

    // A writer that truncates then rewrites is briefly empty; the debounce means we only look once it's whole.
    [Fact]
    public void A_file_half_written_when_the_event_arrived_is_read_whole_at_the_flush()
    {
        var tab = Open("/work/a.txt", "one\n");

        _fs.File.WriteAllText(tab.File.FullName, string.Empty);
        Raise(tab);
        _fs.File.WriteAllText(tab.File.FullName, "one\n");
        Raise(tab);
        Flush();

        Assert.False(tab.ChangedOnDiskMarked);
    }

    [Fact]
    public void A_background_tab_is_marked_without_being_switched_to()
    {
        var background = Open("/work/a.txt", "one\n");
        var front = Open("/work/b.txt", "two\n");

        Change(background, "from the other branch\n");

        Assert.True(background.ChangedOnDiskMarked);
        Assert.Same(front, _group.ActiveTab);
    }

    [Fact]
    public void Saving_a_marked_tab_clears_the_marker()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";
        Change(tab, "from the other branch\n");

        // What #267's Overwrite does once the reviewer picks it.
        tab.Save();

        Assert.False(tab.ChangedOnDiskMarked);
        Assert.Equal("a.txt", tab.Title);
    }

    [Fact]
    public void A_change_that_puts_the_file_back_clears_the_marker()
    {
        var tab = Open("/work/a.txt", "one\n");
        Change(tab, "from the other branch\n");
        Assert.True(tab.ChangedOnDiskMarked);

        Change(tab, "one\n");

        Assert.False(tab.ChangedOnDiskMarked);
    }

    [Fact]
    public void Marking_a_tab_leaves_the_save_guard_armed()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";

        Change(tab, "from the other branch\n");

        Assert.True(tab is { IsDirty: true, ChangedOnDisk: true });
    }

    [Fact]
    public void Opening_and_closing_tabs_follows_and_unfollows_their_directories()
    {
        Open("/work/a.txt", "one\n");
        Assert.Equal(1, _watcher.WatcherCount);

        _group.CloseActive();

        Assert.Equal(0, _watcher.WatcherCount);
    }

    [Fact]
    public void A_change_to_a_file_whose_tab_has_gone_marks_nothing()
    {
        var tab = Open("/work/a.txt", "one\n");
        _fs.File.WriteAllText(tab.File.FullName, "from the other branch\n");
        Raise(tab);
        _group.CloseActive();

        Flush();

        Assert.False(tab.ChangedOnDiskMarked);
        Assert.Empty(_said);
    }

    private EditorTab Open(string path, string content)
    {
        _fs.AddFile(path, new MockFileData(content));
        return _group.OpenOrFocus(_fs.FileInfo.New(path));
    }

    // Through the tab's own FullName, which is exactly what the watcher followed: MockFileSystem
    // roots a POSIX path on Windows, so a literal here wouldn't match the directory it watches.
    private void Change(EditorTab tab, string content)
    {
        _fs.File.WriteAllText(tab.File.FullName, content);
        Raise(tab);
        Flush();
    }

    private void Raise(EditorTab tab) =>
        _fs.Watchers.For(_fs.Path.GetDirectoryName(tab.File.FullName)!).RaiseChanged(tab.File.FullName);

    private void Flush()
    {
        foreach (var flush in _flushes.ToList()) flush();
        _flushes.Clear();
    }
}
