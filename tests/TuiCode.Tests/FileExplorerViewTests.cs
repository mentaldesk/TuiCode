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
    public void ActivateSelected_fires_FileActivated_for_a_selected_file()
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

        IFileInfo? activated = null;
        explorer.FileActivated += (_, f) => activated = f;
        explorer.ActivateSelected();

        Assert.Same(file, activated);
    }

    [Fact]
    public void Enter_key_activates_the_selected_file()
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

        IFileInfo? activated = null;
        explorer.FileActivated += (_, f) => activated = f;
        explorer.NewKeyDownEvent(Key.Enter);

        Assert.Same(file, activated);
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

    [Fact]
    public void NewEntryTarget_is_the_selected_folder()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work/src");

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));
        var src = explorer.GetChildren(explorer.Objects!.Single())
            .OfType<IDirectoryInfo>().Single(d => d.Name == "src");
        explorer.SelectedObject = src;

        Assert.Equal("src", explorer.NewEntryTarget()!.Name);
    }

    [Fact]
    public void NewEntryTarget_is_the_parent_of_the_selected_file()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/src/main.cs", new MockFileData("// code"));

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));
        var src = explorer.GetChildren(explorer.Objects!.Single())
            .OfType<IDirectoryInfo>().Single(d => d.Name == "src");
        explorer.Expand(src);
        var file = explorer.GetChildren(src).OfType<IFileInfo>().Single();
        explorer.SelectedObject = file;

        Assert.Equal("src", explorer.NewEntryTarget()!.Name);
    }

    [Fact]
    public void NewEntryPrefill_is_the_selected_folder_relative_to_root()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work/src");

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));
        var src = explorer.GetChildren(explorer.Objects!.Single())
            .OfType<IDirectoryInfo>().Single(d => d.Name == "src");
        explorer.SelectedObject = src;

        Assert.Equal("src/", explorer.NewEntryPrefill());
    }

    [Fact]
    public void NewEntryTarget_is_the_root_when_nothing_is_selected()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));
        explorer.SelectedObject = null;

        Assert.Equal("work", explorer.NewEntryTarget()!.Name);
    }

    [Fact]
    public void Create_makes_a_file_under_the_selected_folder_and_selects_it()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work/src");

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));
        var src = explorer.GetChildren(explorer.Objects!.Single())
            .OfType<IDirectoryInfo>().Single(d => d.Name == "src");
        explorer.SelectedObject = src;

        // Mirror the host flow: the dialog seeds its field with the prefill, so the
        // confirmed path is root-relative (e.g. "src/widget.cs").
        var node = explorer.Create(explorer.NewEntryPrefill() + "widget.cs", directory: false);

        Assert.IsAssignableFrom<IFileInfo>(node);
        Assert.Equal("widget.cs", node.Name);
        Assert.True(fs.File.Exists(node.FullName), "file should exist on disk");
        Assert.Equal("src", ((IFileInfo)node).Directory!.Name);
        Assert.Same(node, explorer.SelectedObject);
    }

    [Fact]
    public void Create_makes_a_folder_and_selects_it()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));

        var node = explorer.Create("components", directory: true);

        Assert.IsAssignableFrom<IDirectoryInfo>(node);
        Assert.Equal("components", node.Name);
        Assert.True(fs.Directory.Exists(node.FullName), "folder should exist on disk");
        Assert.Same(node, explorer.SelectedObject);
    }

    [Fact]
    public void Create_makes_intermediate_directories_for_a_nested_path()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));

        var node = explorer.Create("a/b/c.txt", directory: false);

        Assert.Equal("c.txt", node.Name);
        Assert.True(fs.File.Exists(node.FullName));
        Assert.True(fs.Directory.Exists(fs.Path.GetDirectoryName(node.FullName)!));
        Assert.Same(node, explorer.SelectedObject);
    }

    [Fact]
    public void Create_throws_when_the_path_already_exists()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/readme.md", new MockFileData("# hi"));

        using var explorer = new FileExplorerView();
        explorer.Open(fs.DirectoryInfo.New("/work"));

        Assert.Throws<IOException>(() => explorer.Create("readme.md", directory: false));
    }
}
