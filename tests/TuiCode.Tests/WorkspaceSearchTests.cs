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
    public void Search_without_a_git_file_list_says_it_is_not_a_repo()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("needle"));

        var result = WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "needle", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.NotARepo);
    }

    [Fact]
    public void Search_with_a_git_file_list_searches_only_the_listed_files()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/.idea/workspace.xml", new MockFileData("needle"));
        fs.AddFile("/work/src/app.cs", new MockFileData("needle"));
        fs.AddFile("/work/bin/kept.txt", new MockFileData("needle"));

        var result = WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "needle", gitFiles: ["src/app.cs", "bin/kept.txt"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.NotARepo);
        Assert.Equal(["kept.txt", "app.cs"], result.Files.Select(f => f.File.Name));
    }

    [Fact]
    public void Search_with_a_git_file_list_skips_listed_files_gone_from_disk_and_nested_repos()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("needle"));
        fs.AddFile("/work/nested/b.txt", new MockFileData("needle"));

        var result = WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "needle", gitFiles: ["a.txt", "deleted.txt", "nested/"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["a.txt"], result.Files.Select(f => f.File.Name));
    }

    [Fact]
    public void Listed_files_come_in_the_order_the_folder_walk_gives()
    {
        var fs = new MockFileSystem();
        string[] paths = ["z.txt", "b/y.txt", "a.txt", "b/c/x.txt", "a/w.txt", "b/a.txt"];
        foreach (var path in paths) fs.AddFile($"/work/{path}", new MockFileData("x"));
        var root = fs.DirectoryInfo.New("/work");

        var listed = WorkspaceSearch.Listed(root, paths, TestContext.Current.CancellationToken);
        var walked = WorkspaceSearch.EnumerateFiles(root, TestContext.Current.CancellationToken);

        Assert.Equal(walked.Select(f => f.FullName), listed.Select(f => f.FullName));
    }

    [Fact]
    public void Search_applies_match_case_and_whole_word()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.cs", new MockFileData("Id id Hidden x.Id"));
        var root = fs.DirectoryInfo.New("/work");

        var result = WorkspaceSearch.Search(root, "Id", new MatchOptions(MatchCase: true, WholeWord: true),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([0, 15], result.Files.Single().Matches.Select(m => m.Match.Column));
    }

    [Fact]
    public void ReplaceAll_honours_match_case_and_whole_word()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.cs", new MockFileData("Id id Hidden x.Id"));

        var (_, occurrences) = WorkspaceSearch.ReplaceAll([fs.FileInfo.New("/work/a.cs")], "Id", "Key",
            new MatchOptions(MatchCase: true, WholeWord: true));

        Assert.Equal(2, occurrences);
        Assert.Equal("Key id Hidden x.Key", fs.File.ReadAllText("/work/a.cs"));
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

        var result = WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "needle", openBuffers: buffers, cancellationToken: TestContext.Current.CancellationToken);

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
            [fs.FileInfo.New("/work/open.txt"), fs.FileInfo.New("/work/closed.txt")], "foo", "bar", openBuffers: buffers);

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

    [Theory]
    [InlineData(5, ".....needle...")]
    [InlineData(3_000, "…............needle" + "...")]
    [InlineData(5_994, "…............needle")]
    public void Match_preview_cuts_a_long_line_to_a_short_row_around_the_hit(int column, string expectedStart)
    {
        var file = new MockFileSystem().FileInfo.New("/work/a.min.js");
        var line = new string('.', column) + "needle" + new string('.', 6_000 - column - 6);

        var preview = new MatchNode(file, new LineMatch(new TuiCode.Abstractions.TextMatch(0, column, 6), line)).ToString();

        Assert.StartsWith("1: " + expectedStart, preview);
        Assert.True(preview.Length < 100, preview);
        Assert.Equal(column + 6 < line.Length, preview.EndsWith('…'));
    }

    [Fact]
    public void Match_preview_leaves_a_short_line_as_it_is()
    {
        var file = new MockFileSystem().FileInfo.New("/work/a.txt");

        var node = new MatchNode(file, new LineMatch(new TuiCode.Abstractions.TextMatch(2, 6, 6), "  say needle here  "));

        Assert.Equal("3: say needle here", node.ToString());
    }

    [Fact]
    public void Search_reports_each_file_it_searches()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("needle"));
        fs.AddFile("/work/b.txt", new MockFileData("nothing"));
        fs.AddFile("/work/c.bin", new MockFileData([0, 1, 2]));
        var searched = 0;

        WorkspaceSearch.Search(fs.DirectoryInfo.New("/work"), "needle", fileSearched: () => searched++,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, searched);
    }

    private sealed class FakeOpenBuffers(string openPath) : IOpenBuffers
    {
        public (string Query, string Replacement)? Replaced { get; private set; }

        public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>();

        public int? ReplaceAll(string fullPath, string query, string replacement, MatchOptions options)
        {
            if (fullPath != openPath) return null;
            Replaced = (query, replacement);
            return 1;
        }
    }
}
