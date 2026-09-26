using Microsoft.Extensions.Logging.Abstractions;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench.Files;
using static TuiCode.Editor.LineChange;

namespace TuiCode.Tests;

// What a change on disk does to a tab: the marker on a dirty one (#268), the new text on a clean one (#269).
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
    public void A_change_someone_else_made_marks_a_dirty_tab_and_says_so_once()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";

        Change(tab, "from the other branch\n");

        Assert.True(tab.ChangedOnDiskMarked);
        Assert.Equal("● a.txt ⚠ ", tab.Title);
        Assert.Equal(["⚠ a.txt changed on disk"], _said);

        Change(tab, "and again\n");
        Assert.Single(_said);
    }

    [Fact]
    public void A_nerd_font_marks_the_tab_and_the_status_line_with_nf_oct_alert()
    {
        _group.IconStyle = FileIconStyle.NerdFont;
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";

        Change(tab, "from the other branch\n");

        Assert.Equal("● a.txt \uf421 ", tab.Title);
        Assert.Equal(["\uf421 a.txt changed on disk"], _said);
    }

    [Fact]
    public void Changing_the_icon_style_restyles_the_tabs_already_marked()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";
        Change(tab, "from the other branch\n");

        _group.IconStyle = FileIconStyle.NerdFont;

        Assert.Equal("● a.txt \uf421 ", tab.Title);
    }

    [Fact]
    public void A_checkout_that_rewrites_the_file_byte_for_byte_changes_nothing()
    {
        var tab = Open("/work/a.txt", "one\n");

        Change(tab, "one\n");

        Assert.False(tab.ChangedOnDiskMarked);
        Assert.Equal("a.txt", tab.Title);
        Assert.Empty(_said);
    }

    [Fact]
    public void Our_own_save_does_not_touch_the_tab_it_just_wrote()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";

        tab.Save();
        Raise(tab);
        Flush();

        Assert.False(tab.ChangedOnDiskMarked);
        Assert.Equal("mine\n", Text(tab));
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

        Assert.Equal("one\n", Text(tab));
        Assert.Empty(_said);
    }

    [Fact]
    public void A_dirty_background_tab_is_marked_without_being_switched_to()
    {
        var background = Open("/work/a.txt", "one\n");
        background.Content = "mine\n";
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
        tab.Content = "mine\n";
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
    public void A_change_to_a_file_whose_tab_has_gone_changes_nothing()
    {
        var tab = Open("/work/a.txt", "one\n");
        _fs.File.WriteAllText(tab.File.FullName, "from the other branch\n");
        Raise(tab);
        _group.CloseActive();

        Flush();

        Assert.False(tab.ChangedOnDiskMarked);
        Assert.Empty(_said);
    }

    [Fact]
    public void A_clean_tab_takes_up_what_is_on_disk_and_says_so_once()
    {
        var tab = Open("/work/a.txt", "one\n");

        Change(tab, "from the other branch\n");

        Assert.Equal("from the other branch\n", Text(tab));
        Assert.Equal(["⟳ Reloaded a.txt — changed on disk"], _said);
    }

    [Fact]
    public void A_reloaded_tab_is_clean_unmarked_and_silent_to_save()
    {
        var tab = Open("/work/a.txt", "one\n");

        Change(tab, "from the other branch\n");

        Assert.False(tab.IsDirty);
        Assert.False(tab.ChangedOnDiskMarked);
        Assert.False(tab.ChangedOnDisk);
        Assert.Equal("a.txt", tab.Title);
    }

    [Fact]
    public void A_reload_keeps_the_cursor_on_the_same_line_and_column()
    {
        var tab = Open("/work/a.txt", "alpha\nbravo\ncharlie\n");
        tab.MoveCursor(1, 3);

        Change(tab, "alpha\nbravo!\ncharlie\n");

        Assert.Equal((1, 3), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void A_reload_of_a_file_that_got_shorter_clamps_the_cursor()
    {
        var tab = Open("/work/a.txt", "alpha\nbravo\ncharlie\n");
        tab.MoveCursor(2, 7);

        Change(tab, "up\n");

        Assert.Equal((1, 0), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void A_reload_resets_the_gutter_baseline_to_the_new_content()
    {
        var tab = Open("/work/a.txt", "alpha\nbravo\n");

        Change(tab, "alpha\nBRAVO\ncharlie\n");

        Assert.All(tab.LineChanges, c => Assert.Equal(None, c));
    }

    [Fact]
    public void A_reload_clears_the_undo_history()
    {
        var tab = Open("/work/a.txt", "alpha\nbravo\n");
        tab.Replace(new TextMatch(0, 0, 5), "ALPHA");
        tab.Save();

        Change(tab, "from the other branch\n");
        tab.SubViews.OfType<EditorTextView>().Single().Undo();

        Assert.Equal("from the other branch\n", Text(tab));
    }

    [Fact]
    public void A_clean_background_tab_reloads_with_nothing_on_screen()
    {
        var background = Open("/work/a.txt", "one\n");
        var front = Open("/work/b.txt", "two\n");

        Change(background, "from the other branch\n");

        Assert.Equal("from the other branch\n", Text(background));
        Assert.Same(front, _group.ActiveTab);
        Assert.Empty(_said);
    }

    [Fact]
    public void A_dirty_tab_is_never_reloaded_behind_your_back()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";

        Change(tab, "from the other branch\n");

        Assert.Equal("mine\n", Text(tab));
        Assert.True(tab.IsDirty);
        Assert.True(tab.ChangedOnDiskMarked);
    }

    [Fact]
    public void A_clean_tab_whose_directory_we_could_not_watch_reloads_when_you_switch_to_it()
    {
        var behind = OpenUnwatched();

        _fs.File.WriteAllText(behind.File.FullName, "from the other branch\n");
        Assert.Equal("one\n", Text(behind));

        _group.Focus(behind.File.FullName);

        Assert.Equal("from the other branch\n", Text(behind));
        Assert.Equal(["⟳ Reloaded a.txt — changed on disk"], _said);
    }

    [Fact]
    public void A_dirty_tab_whose_directory_we_could_not_watch_is_marked_when_you_switch_to_it()
    {
        var behind = OpenUnwatched();
        behind.Content = "mine\n";

        _fs.File.WriteAllText(behind.File.FullName, "from the other branch\n");
        _group.Focus(behind.File.FullName);

        Assert.Equal("mine\n", Text(behind));
        Assert.True(behind.ChangedOnDiskMarked);
    }

    // With a watcher in place the check on activation would be a re-read of a file we're already told about.
    [Fact]
    public void Switching_to_a_tab_in_a_watched_directory_re_reads_nothing()
    {
        var behind = Open("/work/a.txt", "one\n");
        Open("/work/b.txt", "two\n");

        _fs.File.WriteAllText(behind.File.FullName, "from the other branch\n");
        _group.Focus(behind.File.FullName);

        Assert.Equal("one\n", Text(behind));
        Assert.Empty(_said);
    }

    // Two tabs, so there's one to switch away from, in a directory no watcher could be established for.
    private EditorTab OpenUnwatched()
    {
        _fs.Watchers.FailFor.Add(_fs.Path.GetFullPath("/work"));
        var behind = Open("/work/a.txt", "one\n");
        Open("/work/b.txt", "two\n");
        Assert.Equal(0, _watcher.WatcherCount);
        return behind;
    }

    // TextView.Text joins its lines with Environment.NewLine, so compare buffers in LF.
    private static string Text(EditorTab tab) => tab.Content.ReplaceLineEndings("\n");

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
