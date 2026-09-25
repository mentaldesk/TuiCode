using TuiCode.Explorer;

namespace TuiCode.Tests;

public class EntryPathsTests
{
    [Fact]
    public void Prefill_is_empty_when_target_is_the_root()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        var root = fs.DirectoryInfo.New("/work");

        Assert.Equal(string.Empty, EntryPaths.Prefill(root, root));
    }

    [Fact]
    public void Prefill_is_the_target_relative_to_root_with_a_trailing_slash()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work/src/widgets");
        var root = fs.DirectoryInfo.New("/work");
        var target = fs.DirectoryInfo.New("/work/src/widgets");

        Assert.Equal("src/widgets/", EntryPaths.Prefill(root, target));
    }

    [Fact]
    public void Resolve_combines_a_relative_path_against_the_root()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        var root = fs.DirectoryInfo.New("/work");

        var resolved = EntryPaths.Resolve(fs, root, "src/main.cs");

        Assert.Equal(fs.Path.GetFullPath(fs.Path.Combine("/work", "src", "main.cs")), resolved);
    }

    [Fact]
    public void Resolve_accepts_both_slash_styles()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        var root = fs.DirectoryInfo.New("/work");

        Assert.Equal(
            EntryPaths.Resolve(fs, root, "a/b/c.cs"),
            EntryPaths.Resolve(fs, root, "a\\b\\c.cs"));
    }

    [Theory]
    [InlineData("/work", "")]
    [InlineData("/work/src", "src")]
    [InlineData("/work/src/lib/a.cs", "src/lib/a.cs")]
    public void Relative_is_forward_slashed_and_empty_for_the_root(string fullPath, string expected)
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");

        Assert.Equal(expected, EntryPaths.Relative(fs.DirectoryInfo.New("/work"), fs.Path.GetFullPath(fullPath)));
    }
}
