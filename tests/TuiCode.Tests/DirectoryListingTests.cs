using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class DirectoryListingTests
{
    [Fact]
    public void Build_lists_parent_then_directories_then_files_each_sorted()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/root/work");
        fs.AddDirectory("/root/work/src");
        fs.AddDirectory("/root/work/.git");
        fs.AddFile("/root/work/readme.md", new MockFileData("r"));
        fs.AddFile("/root/work/AGENTS.md", new MockFileData("a"));

        var entries = DirectoryListing.Build(fs.DirectoryInfo.New("/root/work"));

        Assert.Collection(
            entries,
            e => Assert.Equal((OpenEntryKind.Parent, "../"), (e.Kind, e.Display)),
            e => Assert.Equal((OpenEntryKind.Directory, ".git/"), (e.Kind, e.Display)),
            e => Assert.Equal((OpenEntryKind.Directory, "src/"), (e.Kind, e.Display)),
            e => Assert.Equal((OpenEntryKind.File, "AGENTS.md"), (e.Kind, e.Display)),
            e => Assert.Equal((OpenEntryKind.File, "readme.md"), (e.Kind, e.Display)));
    }

    [Fact]
    public void Build_omits_the_parent_entry_at_a_filesystem_root()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/");
        fs.AddFile("/top.txt", new MockFileData("t"));

        var entries = DirectoryListing.Build(fs.DirectoryInfo.New("/"));

        Assert.DoesNotContain(entries, e => e.Kind == OpenEntryKind.Parent);
    }

    [Fact]
    public void Build_carries_the_underlying_directory_and_file_info()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work/src");
        fs.AddFile("/work/main.cs", new MockFileData("// code"));

        var entries = DirectoryListing.Build(fs.DirectoryInfo.New("/work"));

        Assert.IsAssignableFrom<IDirectoryInfo>(
            entries.Single(e => e.Kind == OpenEntryKind.Directory).Info);
        Assert.IsAssignableFrom<IFileInfo>(
            entries.Single(e => e.Kind == OpenEntryKind.File).Info);
        Assert.IsAssignableFrom<IDirectoryInfo>(
            entries.Single(e => e.Kind == OpenEntryKind.Parent).Info);
    }
}
