using TuiCode.Explorer;

namespace TuiCode.Tests;

public class FileExplorerFileOperationsTests
{
    private static (MockFileSystem Fs, FileExplorerView Explorer) Open(params string[] files)
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        foreach (var file in files)
        {
            if (file.EndsWith('/')) fs.AddDirectory(file);
            else fs.AddFile(file, new MockFileData(file));
        }
        var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));
        return (fs, explorer);
    }

    private static IFileSystemInfo Node(FileExplorerView explorer, string relativePath)
    {
        IFileSystemInfo current = explorer.Root!;
        foreach (var segment in relativePath.Split('/'))
        {
            explorer.Expand(current);
            current = explorer.GetChildren(current).Single(c => c.Name == segment);
        }
        return current;
    }

    [Fact]
    public void Delete_removes_a_file_and_its_node()
    {
        var (fs, explorer) = Open("/work/a.txt", "/work/b.txt");
        using var _ = explorer;

        explorer.Delete(Node(explorer, "a.txt"));

        Assert.False(fs.File.Exists("/work/a.txt"));
        Assert.Equal(["b.txt"], explorer.GetChildren(explorer.Root!).Select(c => c.Name));
    }

    [Fact]
    public void Delete_removes_a_folder_with_everything_in_it()
    {
        var (fs, explorer) = Open("/work/src/lib/a.cs", "/work/src/b.cs");
        using var _ = explorer;

        explorer.Delete(Node(explorer, "src"));

        Assert.False(fs.Directory.Exists("/work/src"));
        Assert.Empty(explorer.GetChildren(explorer.Root!));
    }

    [Theory]
    [InlineData("b.txt", "c.txt")]
    [InlineData("c.txt", "b.txt")]
    public void Delete_selects_the_next_sibling_or_else_the_previous_one(string deleted, string expected)
    {
        var (_, explorer) = Open("/work/b.txt", "/work/c.txt");
        using var __ = explorer;
        explorer.SelectedObject = Node(explorer, deleted);

        explorer.Delete(explorer.SelectedObject);

        Assert.Equal(expected, explorer.SelectedObject?.Name);
    }

    [Fact]
    public void Delete_selects_the_parent_of_an_only_child()
    {
        var (_, explorer) = Open("/work/src/a.cs");
        using var _ = explorer;
        explorer.SelectedObject = Node(explorer, "src/a.cs");

        explorer.Delete(explorer.SelectedObject);

        Assert.Equal("src", explorer.SelectedObject?.Name);
    }

    [Fact]
    public void Delete_leaves_an_unrelated_selection_alone()
    {
        var (_, explorer) = Open("/work/a.txt", "/work/b.txt");
        using var _ = explorer;
        explorer.SelectedObject = Node(explorer, "b.txt");

        explorer.Delete(Node(explorer, "a.txt"));

        Assert.Equal("b.txt", explorer.SelectedObject?.Name);
    }

    [Fact]
    public void Delete_keeps_sibling_folders_expanded()
    {
        var (_, explorer) = Open("/work/a.txt", "/work/src/lib/x.cs");
        using var _ = explorer;
        var lib = Node(explorer, "src/lib");
        explorer.Expand(lib);

        explorer.Delete(Node(explorer, "a.txt"));

        Assert.True(explorer.IsExpanded(Node(explorer, "src")));
        Assert.True(explorer.IsExpanded(Node(explorer, "src/lib")));
    }

    [Fact]
    public void Delete_refuses_the_root()
    {
        var (fs, explorer) = Open("/work/a.txt");
        using var _ = explorer;

        Assert.Throws<IOException>(() => explorer.Delete(explorer.Root!));
        Assert.True(fs.Directory.Exists("/work"));
    }

    [Fact]
    public void Move_renames_a_file_in_place_and_selects_it()
    {
        var (fs, explorer) = Open("/work/src/a.cs");
        using var _ = explorer;

        var moved = explorer.Move(Node(explorer, "src/a.cs"), "src/b.cs");

        Assert.False(fs.File.Exists("/work/src/a.cs"));
        Assert.Equal("/work/src/a.cs", fs.File.ReadAllText("/work/src/b.cs"));
        Assert.Equal("b.cs", moved.Name);
        Assert.Same(moved, explorer.SelectedObject);
    }

    [Fact]
    public void Move_to_a_new_folder_creates_it()
    {
        var (fs, explorer) = Open("/work/a.cs");
        using var _ = explorer;

        var moved = explorer.Move(Node(explorer, "a.cs"), "src/lib/a.cs");

        Assert.True(fs.File.Exists("/work/src/lib/a.cs"));
        Assert.Same(moved, explorer.SelectedObject);
        Assert.DoesNotContain(explorer.GetChildren(explorer.Root!), c => c.Name == "a.cs");
    }

    [Fact]
    public void Move_carries_a_folder_with_its_contents()
    {
        var (fs, explorer) = Open("/work/src/lib/a.cs");
        using var _ = explorer;

        explorer.Move(Node(explorer, "src"), "code");

        Assert.True(fs.File.Exists("/work/code/lib/a.cs"));
        Assert.False(fs.Directory.Exists("/work/src"));
    }

    [Fact]
    public void Move_only_changing_case_renames()
    {
        var (fs, explorer) = Open("/work/readme.md");
        using var _ = explorer;

        var moved = explorer.Move(Node(explorer, "readme.md"), "README.md");

        Assert.Equal("README.md", moved.Name);
        Assert.Equal(["README.md"], fs.Directory.GetFiles("/work").Select(fs.Path.GetFileName));
    }

    [Fact]
    public void Move_to_the_same_path_returns_the_item_untouched()
    {
        var (_, explorer) = Open("/work/a.cs");
        using var _ = explorer;
        var item = Node(explorer, "a.cs");

        Assert.Same(item, explorer.Move(item, "a.cs"));
    }

    [Theory]
    [InlineData("b.cs")]
    [InlineData("src")]
    public void Move_refuses_a_target_that_exists(string target)
    {
        var (fs, explorer) = Open("/work/a.cs", "/work/b.cs", "/work/src/");
        using var _ = explorer;

        var ex = Assert.Throws<IOException>(() => explorer.Move(Node(explorer, "a.cs"), target));

        Assert.Contains("already exists", ex.Message);
        Assert.True(fs.File.Exists("/work/a.cs"));
    }

    [Theory]
    [InlineData("src/inner")]
    [InlineData("src/lib/inner")]
    public void Move_refuses_to_put_a_folder_inside_itself(string target)
    {
        var (fs, explorer) = Open("/work/src/lib/a.cs");
        using var _ = explorer;

        var ex = Assert.Throws<IOException>(() => explorer.Move(Node(explorer, "src"), target));

        Assert.Contains("into itself", ex.Message);
        Assert.True(fs.File.Exists("/work/src/lib/a.cs"));
    }

    [Theory]
    [InlineData("../a.cs")]
    [InlineData("..")]
    public void Move_refuses_a_target_outside_the_workspace(string target)
    {
        var (fs, explorer) = Open("/work/a.cs");
        using var _ = explorer;

        var ex = Assert.Throws<IOException>(() => explorer.Move(Node(explorer, "a.cs"), target));

        Assert.Contains("outside the workspace", ex.Message);
        Assert.True(fs.File.Exists("/work/a.cs"));
    }

    [Fact]
    public void Move_refuses_the_root()
    {
        var (_, explorer) = Open("/work/a.cs");
        using var _ = explorer;

        Assert.Throws<IOException>(() => explorer.Move(explorer.Root!, "elsewhere"));
    }

    [Fact]
    public void RelativePath_is_forward_slashed()
    {
        var (_, explorer) = Open("/work/src/lib/a.cs");
        using var _ = explorer;

        Assert.Equal("src/lib/a.cs", explorer.RelativePath(Node(explorer, "src/lib/a.cs")));
    }

    [Theory]
    [InlineData("lib", "lib/a.cs")]
    [InlineData("lib/b.cs", "lib/a.cs")]
    [InlineData(null, "a.cs")]
    public void PastePath_puts_the_cut_item_in_a_folder_next_to_a_file_or_at_the_root(string? target, string expected)
    {
        var (_, explorer) = Open("/work/src/a.cs", "/work/lib/b.cs");
        using var _ = explorer;
        explorer.Cut(Node(explorer, "src/a.cs"));

        Assert.Equal(expected, explorer.PastePath(target is null ? null : Node(explorer, target)));
    }

    [Fact]
    public void PastePath_is_null_with_nothing_cut()
    {
        var (_, explorer) = Open("/work/a.cs");
        using var _ = explorer;

        Assert.Null(explorer.PastePath(null));
    }

    [Fact]
    public void Cut_replaces_an_earlier_cut()
    {
        var (_, explorer) = Open("/work/a.cs", "/work/b.cs");
        using var _ = explorer;

        explorer.Cut(Node(explorer, "a.cs"));
        explorer.Cut(Node(explorer, "b.cs"));

        Assert.Equal("b.cs", explorer.PendingCut?.Name);
    }

    [Fact]
    public void Cut_refuses_the_root()
    {
        var (_, explorer) = Open("/work/a.cs");
        using var _ = explorer;

        var ex = Assert.Throws<IOException>(() => explorer.Cut(explorer.Root!));

        Assert.Equal("The workspace root can't be cut.", ex.Message);
        Assert.Null(explorer.PendingCut);
    }

    [Theory]
    [InlineData("src/a.cs")]
    [InlineData("src")]
    public void Deleting_the_cut_item_or_its_folder_clears_the_cut(string deleted)
    {
        var (_, explorer) = Open("/work/src/a.cs");
        using var _ = explorer;
        explorer.Cut(Node(explorer, "src/a.cs"));

        explorer.Delete(Node(explorer, deleted));

        Assert.Null(explorer.PendingCut);
    }

    [Theory]
    [InlineData("src/a.cs", "src/b.cs")]
    [InlineData("src", "lib")]
    public void Renaming_the_cut_item_or_its_folder_clears_the_cut(string renamed, string to)
    {
        var (_, explorer) = Open("/work/src/a.cs");
        using var _ = explorer;
        explorer.Cut(Node(explorer, "src/a.cs"));

        explorer.Move(Node(explorer, renamed), to);

        Assert.Null(explorer.PendingCut);
    }

    [Fact]
    public void Deleting_something_else_keeps_the_cut()
    {
        var (_, explorer) = Open("/work/a.cs", "/work/b.cs");
        using var _ = explorer;
        explorer.Cut(Node(explorer, "a.cs"));

        explorer.Delete(Node(explorer, "b.cs"));

        Assert.Equal("a.cs", explorer.PendingCut?.Name);
    }

    [Fact]
    public void Opening_another_folder_clears_the_cut()
    {
        var (fs, explorer) = Open("/work/a.cs", "/other/");
        using var _ = explorer;
        explorer.Cut(Node(explorer, "a.cs"));

        explorer.Open(fs.DirectoryInfo.New("/other"));

        Assert.Null(explorer.PendingCut);
    }
}
