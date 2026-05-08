using TuiCode.Explorer;

namespace TuiCode.Tests;

public class FileExplorerViewTests
{
    [Fact]
    public void Open_roots_the_tree_at_the_given_directory()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        fs.AddFile("/work/readme.md", new MockFileData("# hi"));
        fs.AddDirectory("/work/src");
        fs.AddFile("/work/src/main.cs", new MockFileData("// code"));

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));

        var root = Assert.Single(explorer.Objects!);
        Assert.Equal("work", root.Name);
        Assert.True(explorer.IsExpanded(root), "root directory should be expanded after Open");

        var rootChildren = explorer.GetChildren(root).ToList();
        Assert.Contains(rootChildren, c => c.Name == "src" && c is IDirectoryInfo);
        Assert.Contains(rootChildren, c => c.Name == "readme.md" && c is IFileInfo);
    }

    [Fact]
    public void ActivateSelected_fires_FileActivated_with_full_path_for_file_selection()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        fs.AddFile("/work/readme.md", new MockFileData("# hi"));

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));

        var file = explorer
            .GetChildren(explorer.Objects!.Single())
            .OfType<IFileInfo>()
            .Single(f => f.Name == "readme.md");
        explorer.SelectedObject = file;

        string? activatedPath = null;
        explorer.FileActivated += (_, path) => activatedPath = path;
        explorer.ActivateSelected();

        Assert.Equal(file.FullName, activatedPath);
    }

    [Fact]
    public void ActivateSelected_does_nothing_when_a_directory_is_selected()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work/src");

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));
        explorer.SelectedObject = explorer.Objects!.Single();

        var fired = false;
        explorer.FileActivated += (_, _) => fired = true;
        explorer.ActivateSelected();

        Assert.False(fired);
    }
}
