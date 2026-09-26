using Microsoft.Extensions.Logging.Abstractions;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench.Files;
using static TuiCode.Editor.LineChange;

namespace TuiCode.Tests;

// What a change on disk does to a tab: the marker on a dirty one (#268), the new text on a clean one (#269),
// and neither on a file that has gone (#271).
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

        Assert.Equal(DiskState.Changed, tab.DiskMarker);
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

        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
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

        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
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

        Assert.Equal(DiskState.Changed, background.DiskMarker);
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

        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
        Assert.Equal("a.txt", tab.Title);
    }

    [Fact]
    public void A_change_that_puts_the_file_back_clears_the_marker()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";
        Change(tab, "from the other branch\n");
        Assert.Equal(DiskState.Changed, tab.DiskMarker);

        Change(tab, "one\n");

        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
    }

    [Fact]
    public void Marking_a_tab_leaves_the_save_guard_armed()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";

        Change(tab, "from the other branch\n");

        Assert.True(tab is { IsDirty: true, DiskNow: DiskState.Changed });
    }

    [Fact]
    public void A_deleted_file_marks_its_tab_and_keeps_the_buffer()
    {
        var tab = Open("/work/a.txt", "one\n");

        Delete(tab);

        Assert.Equal(DiskState.Gone, tab.DiskMarker);
        Assert.Equal("a.txt \u2298 ", tab.Title);
        Assert.Equal("one\n", Text(tab));
        Assert.Equal(["\u2298 a.txt no longer exists on disk \u2014 Ctrl+S writes it back"], _said);
    }

    [Fact]
    public void A_deleted_file_says_so_once()
    {
        var tab = Open("/work/a.txt", "one\n");

        Delete(tab);
        Raise(tab);
        Flush();

        Assert.Single(_said);
    }

    [Fact]
    public void A_dirty_tab_whose_file_was_deleted_keeps_both_marks_and_every_edit()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";

        Delete(tab);

        Assert.Equal("\u25cf a.txt \u2298 ", tab.Title);
        Assert.Equal("mine\n", Text(tab));
        // Closing it warns the way any dirty tab does; nothing about the deletion touched that.
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public void A_nerd_font_marks_a_deleted_file_with_nf_fa_ban()
    {
        _group.IconStyle = FileIconStyle.NerdFont;
        var tab = Open("/work/a.txt", "one\n");

        Delete(tab);

        Assert.Equal("a.txt \uf05e ", tab.Title);
        Assert.Equal(["\uf05e a.txt no longer exists on disk \u2014 Ctrl+S writes it back"], _said);
    }

    [Fact]
    public void A_file_renamed_away_has_gone_rather_than_followed_to_its_new_name()
    {
        var tab = Open("/work/a.txt", "one\n");
        var moved = tab.File.FullName;

        _fs.File.Move(moved, _fs.Path.Combine(_fs.Path.GetDirectoryName(moved)!, "b.txt"));
        _fs.Watchers.For(_fs.Path.GetDirectoryName(moved)!).RaiseRenamedAway(moved);
        Flush();

        Assert.Equal(DiskState.Gone, tab.DiskMarker);
        Assert.Equal(moved, tab.File.FullName);
    }

    [Fact]
    public void A_deletion_leaves_the_undo_history_alone()
    {
        var tab = Open("/work/a.txt", "alpha\n");
        tab.Replace(new TextMatch(0, 0, 5), "ALPHA");

        Delete(tab);
        tab.SubViews.OfType<EditorTextView>().Single().Undo();

        Assert.Equal("alpha\n", Text(tab));
    }

    [Fact]
    public void Saving_a_deleted_file_writes_it_back_and_clears_the_marker()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";
        Delete(tab);

        tab.Save();

        Assert.Equal("mine\n", _fs.File.ReadAllText(tab.File.FullName));
        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
        Assert.Equal("a.txt", tab.Title);
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void Saving_a_deleted_file_recreates_the_directory_that_went_with_it()
    {
        var tab = Open("/work/sub/a.txt", "one\n");
        tab.Content = "mine\n";
        _fs.Directory.Delete(_fs.Path.GetDirectoryName(tab.File.FullName)!, recursive: true);
        Raise(tab);
        Flush();
        Assert.Equal(DiskState.Gone, tab.DiskMarker);

        tab.Save();

        Assert.Equal("mine\n", _fs.File.ReadAllText(tab.File.FullName));
        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
    }

    // Checking the branch out again puts the file back byte for byte, so there's nothing to take up.
    [Fact]
    public void A_clean_tab_whose_file_comes_back_drops_the_marker()
    {
        var tab = Open("/work/a.txt", "one\n");
        Delete(tab);

        Change(tab, "one\n");

        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
        Assert.Equal("one\n", Text(tab));
        Assert.Single(_said);
    }

    [Fact]
    public void A_clean_tab_whose_file_comes_back_different_reloads_it()
    {
        var tab = Open("/work/a.txt", "one\n");
        Delete(tab);

        Change(tab, "from the other branch\n");

        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
        Assert.Equal("from the other branch\n", Text(tab));
    }

    [Fact]
    public void A_dirty_tab_whose_file_comes_back_is_marked_changed_instead()
    {
        var tab = Open("/work/a.txt", "one\n");
        tab.Content = "mine\n";
        Delete(tab);

        Change(tab, "from the other branch\n");

        Assert.Equal(DiskState.Changed, tab.DiskMarker);
        Assert.Equal("\u25cf a.txt \u26a0 ", tab.Title);
        Assert.Equal("mine\n", Text(tab));
    }

    [Fact]
    public void The_explorer_is_told_about_a_file_that_has_gone_too()
    {
        var tab = Open("/work/a.txt", "one\n");

        Delete(tab);

        Assert.Equal([tab.File.FullName], _explorer.MarkedOnDisk);
    }

    // Workbench.Delete closes the tabs itself; the event that follows mustn't put a marker back on one of them.
    [Fact]
    public void Deleting_a_file_from_inside_the_editor_closes_its_tab_and_leaves_no_marker()
    {
        var tab = Open("/work/a.txt", "one\n");

        _fs.File.Delete(tab.File.FullName);
        Raise(tab);
        _group.CloseUnder(tab.File.FullName);
        Flush();

        Assert.Empty(_group.Tabs);
        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
        Assert.Empty(_explorer.MarkedOnDisk);
        Assert.Empty(_said);
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

        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
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
        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
        Assert.Equal(DiskState.Unchanged, tab.DiskNow);
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
        Assert.Equal(DiskState.Changed, tab.DiskMarker);
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
        Assert.Equal(DiskState.Changed, behind.DiskMarker);
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

    private void Delete(EditorTab tab)
    {
        _fs.File.Delete(tab.File.FullName);
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
