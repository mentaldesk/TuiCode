using TuiCode.Search;

namespace TuiCode.Tests;

public class WorkspaceSearchTests
{
    [Fact]
    public void Search_finds_matches_across_nested_files_with_their_line_text()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/readme.md", new MockFileData("hello\nbye\n"));
        fs.AddFile("/work/src/app.cs", new MockFileData("// say Hello\n"));
        fs.AddFile("/work/src/other.cs", new MockFileData("nothing here\n"));

        var result = WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.MatchCount);
        Assert.Equal(["readme.md", "app.cs"], result.Files.Select(f => f.File.Name));
        var hit = result.Files[1].Matches.Single();
        Assert.Equal(0, hit.Match.Row);
        Assert.Equal(7, hit.Match.Column);
        Assert.Equal("// say Hello", hit.LineText);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    public void Search_skips_excluded_directories(string excluded)
    {
        var fs = new MockFileSystem();
        fs.AddFile($"/work/{excluded}/file.txt", new MockFileData("needle"));
        fs.AddFile("/work/keep.txt", new MockFileData("needle"));

        var result = WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "needle", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["keep.txt"], result.Files.Select(f => f.File.Name));
    }

    [Fact]
    public void Search_skips_binary_files()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/blob.bin", new MockFileData([0x6E, 0x65, 0x65, 0x64, 0x6C, 0x65, 0x00, 0x01]));

        var result = WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "needle", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Files);
    }

    [Fact]
    public void Search_reads_open_buffers_instead_of_disk()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("on disk"));
        var buffers = new Dictionary<string, string> { [fs.Path.GetFullPath("/work/a.txt")] = "unsaved needle" };

        var result = WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "needle", buffers, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(result.Files);
    }

    [Fact]
    public void Search_stops_at_the_match_limit_and_flags_truncation()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("x x x"));
        fs.AddFile("/work/b.txt", new MockFileData("x x x"));

        var result = WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "x", maxMatches: 4, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Truncated);
        Assert.Equal(4, result.MatchCount);
    }

    [Fact]
    public void Search_honours_cancellation()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("x"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "x", cancellationToken: cts.Token));
    }

    [Fact]
    public void ReplaceAll_rewrites_files_preserving_line_endings_and_bom()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/crlf.txt", new MockFileData([0xEF, 0xBB, 0xBF, .. "Foo\r\nfoo\r\n"u8.ToArray()]));
        var file = fs.FileInfo.New("/work/crlf.txt");

        var (files, occurrences) = WorkspaceSearch.ReplaceAll([file], "foo", "bar");

        Assert.Equal((1, 2), (files, occurrences));
        Assert.Equal([0xEF, 0xBB, 0xBF, .. "bar\r\nbar\r\n"u8.ToArray()], fs.File.ReadAllBytes("/work/crlf.txt"));
    }

    [Fact]
    public void ReplaceAll_edits_open_buffers_in_place_and_leaves_their_files_untouched()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/open.txt", new MockFileData("foo"));
        fs.AddFile("/work/closed.txt", new MockFileData("foo"));
        var buffers = new FakeOpenBuffers(fs.Path.GetFullPath("/work/open.txt"));

        var (files, occurrences) = WorkspaceSearch.ReplaceAll(
            [fs.FileInfo.New("/work/open.txt"), fs.FileInfo.New("/work/closed.txt")], "foo", "bar", buffers);

        Assert.Equal((2, 2), (files, occurrences));
        Assert.Equal("foo", fs.File.ReadAllText("/work/open.txt"));
        Assert.Equal("bar", fs.File.ReadAllText("/work/closed.txt"));
        Assert.Equal(("foo", "bar"), buffers.Replaced);
    }

    [Fact]
    public void Result_tree_nests_files_under_their_directories()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/top.txt", new MockFileData("x"));
        fs.AddFile("/work/src/deep/a.txt", new MockFileData("x\nx"));
        var root = fs.DirectoryInfo.New("/work");

        var nodes = SearchResultTree.Build(root, WorkspaceSearch.Search(root, "x", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["top.txt (1)", "src"], nodes.Select(n => n.ToString()));
        var deep = Assert.IsType<DirectoryNode>(Assert.Single(nodes[1].Children));
        var file = Assert.IsType<FileNode>(Assert.Single(deep.Children));
        Assert.Equal(["1: x", "2: x"], file.Children.Select(n => n.ToString()));
    }

    [Fact]
    public void Match_preview_trims_leading_text_so_the_hit_stays_visible()
    {
        var file = new MockFileSystem().FileInfo.New("/work/a.txt");
        var line = "    " + new string('.', 40) + "needle";

        var node = new MatchNode(file, new LineMatch(new TuiCode.Abstractions.TextMatch(4, 44, 6), line));

        Assert.Equal("5: …............needle", node.ToString());
    }

    private sealed class FakeOpenBuffers(string openPath) : IOpenBuffers
    {
        public (string Query, string Replacement)? Replaced { get; private set; }

        public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>();

        public int? ReplaceAll(string fullPath, string query, string replacement)
        {
            if (fullPath != openPath) return null;
            Replaced = (query, replacement);
            return 1;
        }
    }
}
