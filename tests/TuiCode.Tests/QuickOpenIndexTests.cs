using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class QuickOpenIndexTests
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public void Scan_lists_everything_below_the_root_breadth_first_skipping_excluded_directories()
    {
        _fs.AddFile("/work/readme.md", new MockFileData(""));
        _fs.AddFile("/work/src/App/Program.cs", new MockFileData(""));
        _fs.AddFile("/work/src/lib.cs", new MockFileData(""));
        _fs.AddFile("/work/.git/HEAD", new MockFileData(""));
        _fs.AddFile("/work/src/App/bin/App.dll", new MockFileData(""));

        var index = QuickOpenIndex.Scan(_fs.DirectoryInfo.New("/work"));

        Assert.False(index.Truncated);
        Assert.Equal(
            ["src/", "readme.md", "src/App/", "src/lib.cs", "src/App/Program.cs"],
            index.Entries.Select(e => e.Display));
    }

    [Fact]
    public void Scan_stops_at_the_cap_keeping_the_shallowest_entries()
    {
        _fs.AddFile("/work/a.txt", new MockFileData(""));
        _fs.AddFile("/work/b.txt", new MockFileData(""));
        _fs.AddFile("/work/deep/c.txt", new MockFileData(""));

        var index = QuickOpenIndex.Scan(_fs.DirectoryInfo.New("/work"), maxEntries: 3);

        Assert.True(index.Truncated);
        Assert.Equal(["deep/", "a.txt", "b.txt"], index.Entries.Select(e => e.Display));
    }

    [Fact]
    public void Filter_ranks_better_matches_then_shorter_names_then_shallower_paths()
    {
        _fs.AddFile("/work/tests/OpenViewTests.cs", new MockFileData(""));
        _fs.AddFile("/work/src/Navigation/OpenView.cs", new MockFileData(""));
        _fs.AddFile("/work/OverView.cs", new MockFileData(""));
        _fs.AddFile("/work/docs/OpenView.cs", new MockFileData(""));
        _fs.AddFile("/work/notes.txt", new MockFileData(""));
        var index = QuickOpenIndex.Scan(_fs.DirectoryInfo.New("/work"));

        Assert.Equal(
            ["OverView.cs", "docs/OpenView.cs", "src/Navigation/OpenView.cs", "tests/OpenViewTests.cs"],
            index.Filter("ov").Select(e => e.Display));
    }

    [Fact]
    public void Filter_with_a_slash_matches_path_segments_and_includes_directories()
    {
        _fs.AddFile("/work/src/Navigation/OpenView.cs", new MockFileData(""));
        _fs.AddFile("/work/tests/OpenViewTests.cs", new MockFileData(""));
        var index = QuickOpenIndex.Scan(_fs.DirectoryInfo.New("/work"));

        Assert.Equal(["src/Navigation/OpenView.cs"], index.Filter("s/ov").Select(e => e.Display));
        Assert.Equal(["src/Navigation/"], index.Filter("nav").Select(e => e.Display));
    }
}
