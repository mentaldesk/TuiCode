using TuiCode.Abstractions;

namespace TuiCode.Tests;

public class FilePathsTests
{
    [Theory]
    [InlineData("/work/src", "/work/src", true)]
    [InlineData("/work/src/a.cs", "/work/src", true)]
    [InlineData("/work/src/a.cs", "/work/src/", true)]
    [InlineData(@"C:\work\src\a.cs", @"C:\work\src", true)]
    [InlineData("/work/srcs/a.cs", "/work/src", false)]
    [InlineData("/work", "/work/src", false)]
    [InlineData("/work/Src/a.cs", "/work/src", false)]
    public void IsSameOrUnder_matches_whole_segments_only(string path, string ancestor, bool expected)
    {
        Assert.Equal(expected, FilePaths.IsSameOrUnder(path, ancestor));
    }

    [Theory]
    [InlineData("/work/a.cs", "/work/a.cs", "/work/b.cs", "/work/b.cs")]
    [InlineData("/work/src/lib/a.cs", "/work/src", "/work/code", "/work/code/lib/a.cs")]
    [InlineData("/work/other/a.cs", "/work/src", "/work/code", "/work/other/a.cs")]
    [InlineData("/work/srcs/a.cs", "/work/src", "/work/code", "/work/srcs/a.cs")]
    public void Rebase_moves_paths_at_or_under_the_source(string path, string from, string to, string expected)
    {
        Assert.Equal(expected, FilePaths.Rebase(path, from, to));
    }

    [Theory]
    [InlineData("components/", true)]
    [InlineData("src/utils/", true)]
    [InlineData("notes.txt", false)]
    [InlineData("Makefile", false)]
    [InlineData("a/b/c.cs", false)]
    public void IsDirectoryPath_keys_off_a_trailing_slash(string path, bool expected)
    {
        Assert.Equal(expected, FilePaths.IsDirectoryPath(path));
    }
}
