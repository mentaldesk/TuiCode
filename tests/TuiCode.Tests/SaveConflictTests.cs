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

        Assert.False(tab.ChangedOnDisk);
    }

    [Fact]
    public void A_file_someone_else_rewrote_has_changed()
    {
        var tab = Open("one\n");
        tab.Content = "edited\n";

        _fs.File.WriteAllText(Path, "from the other branch\n");

        Assert.True(tab.ChangedOnDisk);
    }

    [Fact]
    public void A_newer_mtime_over_identical_content_has_not_changed()
    {
        var tab = Open("one\n");

        _fs.File.SetLastWriteTimeUtc(Path, _fs.File.GetLastWriteTimeUtc(Path).AddHours(1));

        Assert.False(tab.ChangedOnDisk);
    }

    // The bytes differ (a BOM in front) but ReadAllText gives back the same text, so the
    // length screen fires and the content comparison clears it.
    [Fact]
    public void A_different_length_over_identical_content_has_not_changed()
    {
        var tab = Open("one\n");

        _fs.File.WriteAllBytes(Path, [0xEF, 0xBB, 0xBF, .. "one\n"u8.ToArray()]);

        Assert.Equal("one\n", _fs.File.ReadAllText(Path));
        Assert.False(tab.ChangedOnDisk);
    }

    [Fact]
    public void A_deleted_file_has_not_changed_because_there_is_nothing_left_to_overwrite()
    {
        var tab = Open("one\n");
        tab.Content = "edited\n";

        _fs.File.Delete(Path);

        Assert.False(tab.ChangedOnDisk);
    }

    [Fact]
    public void Save_recreates_a_deleted_file()
    {
        var tab = Open("one\n");
        tab.Content = "edited\n";
        _fs.File.Delete(Path);

        tab.Save();

        Assert.Equal("edited\n", _fs.File.ReadAllText(Path));
        Assert.False(tab.ChangedOnDisk);
    }

    [Fact]
    public void Save_records_what_it_wrote_so_the_next_save_has_nothing_to_warn_about()
    {
        var tab = Open("one\n");
        tab.Content = "edited\n";
        tab.Save();

        tab.Content = "edited again\n";

        Assert.False(tab.ChangedOnDisk);
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

        Assert.False(tab.ChangedOnDisk);
    }

    private const string Path = "/work/a.txt";

    private EditorTab Open(string content)
    {
        _fs.AddFile(Path, new MockFileData(content));
        return new EditorTab(_fs.FileInfo.New(Path));
    }
}
