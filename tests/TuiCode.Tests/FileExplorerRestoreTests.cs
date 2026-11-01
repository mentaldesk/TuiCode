using TuiCode.Explorer;

namespace TuiCode.Tests;

// Putting back the row selected when the folder was last open (#645).
public sealed class FileExplorerRestoreTests : IDisposable
{
    private readonly MockFileSystem _fs = new();
    private readonly FileExplorerView _explorer = new();

    public FileExplorerRestoreTests()
    {
        _fs.AddDirectory("/work");
        foreach (var file in new[] { "/work/src/lib/a.cs", "/work/src/lib/c.cs", "/work/src/b.cs", "/work/docs/x.md", "/work/z.txt" })
            _fs.AddFile(file, new MockFileData(""));
        _fs.AddDirectory("/work/src/empty");
    }

    [Fact]
    public void Restore_selects_the_entry_and_expands_only_the_folders_above_it()
    {
        Open();

        _explorer.Restore(Full("/work/src/lib/c.cs"), folder: false);

        Assert.Equal(Full("/work/src/lib/c.cs"), _explorer.SelectedObject?.FullName);
        Assert.Equal([Full("/work"), Full("/work/src"), Full("/work/src/lib")], _explorer.ExpandedFolders.Order());
    }

    [Fact]
    public void Restore_selects_a_folder_without_expanding_it()
    {
        Open();

        _explorer.Restore(Full("/work/src/lib"), folder: true);

        Assert.Equal(Full("/work/src/lib"), _explorer.SelectedObject?.FullName);
        Assert.DoesNotContain(Full("/work/src/lib"), _explorer.ExpandedFolders);
    }

    [Fact]
    public void Restore_selects_the_entry_now_in_the_place_of_one_that_is_gone()
    {
        Open();

        _explorer.Restore(Full("/work/src/lib/b.cs"), folder: false);

        Assert.Equal(Full("/work/src/lib/c.cs"), _explorer.SelectedObject?.FullName);
    }

    [Fact]
    public void Restore_selects_the_last_entry_in_the_folder_when_nothing_comes_after_the_one_that_is_gone()
    {
        Open();

        _explorer.Restore(Full("/work/src/lib/d.cs"), folder: false);

        Assert.Equal(Full("/work/src/lib/c.cs"), _explorer.SelectedObject?.FullName);
    }

    [Fact]
    public void Restore_selects_the_folder_when_it_is_empty()
    {
        Open();

        _explorer.Restore(Full("/work/src/empty/gone.cs"), folder: false);

        Assert.Equal(Full("/work/src/empty"), _explorer.SelectedObject?.FullName);
    }

    [Fact]
    public void Restore_keeps_a_gone_folder_among_the_folders()
    {
        Open();

        _explorer.Restore(Full("/work/src/aaa"), folder: true);

        Assert.Equal(Full("/work/src/empty"), _explorer.SelectedObject?.FullName);
    }

    [Fact]
    public void Restore_looks_for_a_gone_folder_on_the_path_in_its_parent()
    {
        Open();

        _explorer.Restore(Full("/work/src/gone/a.cs"), folder: false);

        Assert.Equal(Full("/work/src/lib"), _explorer.SelectedObject?.FullName);
    }

    [Fact]
    public void Restore_ignores_a_path_outside_the_tree()
    {
        Open();

        _explorer.Restore(Full("/elsewhere/a.cs"), folder: false);

        Assert.Equal([Full("/work")], _explorer.ExpandedFolders);
    }

    public void Dispose() => _explorer.Dispose();

    private void Open() => _explorer.Open(_fs.DirectoryInfo.New("/work"));

    private string Full(string path) => _fs.Path.GetFullPath(path);
}
