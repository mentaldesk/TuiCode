using TuiCode.Explorer;

namespace TuiCode.Tests;

public class NewEntryPathsTests
{
    [Fact]
    public void Prefill_is_empty_when_target_is_the_root()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        var root = fs.DirectoryInfo.New("/work");

        Assert.Equal(string.Empty, NewEntryPaths.Prefill(root, root));
    }

    [Fact]
    public void Prefill_is_the_target_relative_to_root_with_a_trailing_slash()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work/src/widgets");
        var root = fs.DirectoryInfo.New("/work");
        var target = fs.DirectoryInfo.New("/work/src/widgets");

        Assert.Equal("src/widgets/", NewEntryPaths.Prefill(root, target));
    }

    [Theory]
    [InlineData("components/", true)]
    [InlineData("src/utils/", true)]
    [InlineData("notes.txt", false)]
    [InlineData("Makefile", false)]
    [InlineData("a/b/c.cs", false)]
    public void IsDirectoryPath_keys_off_a_trailing_slash(string path, bool expected)
    {
        Assert.Equal(expected, NewEntryPaths.IsDirectoryPath(path));
    }

    [Fact]
    public void Resolve_combines_a_relative_path_against_the_root()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        var root = fs.DirectoryInfo.New("/work");

        var resolved = NewEntryPaths.Resolve(fs, root, "src/main.cs");

        Assert.Equal(fs.Path.GetFullPath(fs.Path.Combine("/work", "src", "main.cs")), resolved);
    }

    [Fact]
    public void Resolve_accepts_both_slash_styles()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        var root = fs.DirectoryInfo.New("/work");

        Assert.Equal(
            NewEntryPaths.Resolve(fs, root, "a/b/c.cs"),
            NewEntryPaths.Resolve(fs, root, "a\\b\\c.cs"));
    }
}
