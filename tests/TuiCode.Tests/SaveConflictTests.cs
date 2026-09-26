using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Tests;

// What a tab remembers about the file on disk, so Ctrl+S can tell its own last write from
// someone else's (#267).
public class SaveConflictTests
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public void A_file_nobody_has_touched_has_not_changed()
    {
        var tab = Open("one\n");
        tab.Content = "edited\n";

        Assert.Equal(DiskState.Unchanged, tab.DiskNow);
    }

    [Fact]
    public void A_file_someone_else_rewrote_has_changed()
    {
        var tab = Open("one\n");
        tab.Content = "edited\n";

        _fs.File.WriteAllText(Path, "from the other branch\n");

        Assert.Equal(DiskState.Changed, tab.DiskNow);
    }

    [Fact]
    public void A_newer_mtime_over_identical_content_has_not_changed()
    {
        var tab = Open("one\n");

        _fs.File.SetLastWriteTimeUtc(Path, _fs.File.GetLastWriteTimeUtc(Path).AddHours(1));

        Assert.Equal(DiskState.Unchanged, tab.DiskNow);
    }

    // The bytes differ (a BOM in front) but ReadAllText gives back the same text, so the
    // length screen fires and the content comparison clears it.
    [Fact]
    public void A_different_length_over_identical_content_has_not_changed()
    {
        var tab = Open("one\n");

        _fs.File.WriteAllBytes(Path, [0xEF, 0xBB, 0xBF, .. "one\n"u8.ToArray()]);

        Assert.Equal("one\n", _fs.File.ReadAllText(Path));
        Assert.Equal(DiskState.Unchanged, tab.DiskNow);
    }

    // Gone, not changed: there's nothing left to overwrite, so Ctrl+S has nothing to ask about (#271).
    [Fact]
    public void A_deleted_file_is_gone_rather_than_changed()
    {
        var tab = Open("one\n");
        tab.Content = "edited\n";

        _fs.File.Delete(Path);

        Assert.Equal(DiskState.Gone, tab.DiskNow);
    }

    [Fact]
    public void Save_recreates_a_deleted_file()
    {
        var tab = Open("one\n");
        tab.Content = "edited\n";
        _fs.File.Delete(Path);

        tab.Save();

        Assert.Equal("edited\n", _fs.File.ReadAllText(Path));
        Assert.Equal(DiskState.Unchanged, tab.DiskNow);
    }

    [Fact]
    public void Save_records_what_it_wrote_so_the_next_save_has_nothing_to_warn_about()
    {
        var tab = Open("one\n");
        tab.Content = "edited\n";
        tab.Save();

        tab.Content = "edited again\n";

        Assert.Equal(DiskState.Unchanged, tab.DiskNow);
    }

    [Fact]
    public void Following_a_rename_does_not_count_as_someone_else_changing_the_file()
    {
        using var group = new EditorGroup();
        _fs.AddFile(Path, new MockFileData("one\n"));
        var tab = group.OpenOrFocus(_fs.FileInfo.New(Path));
        tab.Content = "edited\n";

        _fs.File.Move(Path, "/work/b.txt");
        group.Relocate(Path, "/work/b.txt");

        Assert.Equal(DiskState.Unchanged, tab.DiskNow);
    }

    // Reload as a way out of the conflict (#270): unlike #269's clean-tab reload, this one has edits to drop.
    [Fact]
    public void Reloading_over_unsaved_edits_leaves_the_tab_clean()
    {
        var tab = Open("one\n");
        tab.Content = "mine\n";
        _fs.File.WriteAllText(Path, "from the other branch\n");

        Assert.True(tab.Reload());

        Assert.Equal("from the other branch\n", tab.Content.ReplaceLineEndings("\n"));
        Assert.False(tab.IsDirty);
        Assert.Equal(DiskState.Unchanged, tab.DiskNow);
    }

    [Fact]
    public void Reloading_over_unsaved_edits_clears_the_undo_history()
    {
        var tab = Open("one\n");
        tab.Content = "mine\n";
        _fs.File.WriteAllText(Path, "from the other branch\n");

        tab.Reload();
        tab.SubViews.OfType<EditorTextView>().Single().Undo();

        Assert.Equal("from the other branch\n", tab.Content.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Reloading_over_unsaved_edits_says_the_tab_stopped_being_dirty()
    {
        var tab = Open("one\n");
        tab.Content = "mine\n";
        _fs.File.WriteAllText(Path, "from the other branch\n");
        var told = 0;
        tab.DirtyChanged += (_, _) => told++;

        tab.Reload();

        Assert.Equal(1, told);
    }

    private const string Path = "/work/a.txt";

    private EditorTab Open(string content)
    {
        _fs.AddFile(Path, new MockFileData(content));
        return new EditorTab(_fs.FileInfo.New(Path));
    }
}
