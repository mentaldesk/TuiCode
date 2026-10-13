using TuiCode.Search;

namespace TuiCode.Tests;

// Not hosted in a running app, so the view searches synchronously.
public class SearchViewTests
{
    [Fact]
    public void Typing_a_query_searches_the_root_and_reports_counts()
    {
        var (view, _) = Build(("/work/a.txt", "needle needle"), ("/work/sub/b.txt", "needle"));

        view.Query = "needle";

        Assert.Equal(3, view.Result.MatchCount);
        Assert.Equal("3 in 2 files (not a git repo)", view.StatusText);
    }

    [Fact]
    public void Outside_a_git_repo_the_fixed_skip_list_applies_and_ignored_files_is_disabled()
    {
        var (view, _) = Build(("/work/a.txt", "needle"), ("/work/bin/b.txt", "needle"));

        view.Query = "needle";

        Assert.Equal("1 in 1 file (not a git repo)", view.StatusText);
        Assert.False(view.IgnoredFilesAvailable);
        view.ToggleIgnoredFiles();
        Assert.False(view.IncludeIgnored);
    }

    [Fact]
    public void In_a_git_repo_only_the_files_git_lists_are_searched()
    {
        var git = new FakeGitCli { ListedFiles = ignored => ignored ? ["a.txt", ".idea/b.txt"] : ["a.txt"] };
        var (view, _) = Build(git, ("/work/a.txt", "needle"), ("/work/.idea/b.txt", "needle"));

        view.Query = "needle";

        Assert.True(view.IgnoredFilesAvailable);
        Assert.Equal("1 in 1 file", view.StatusText);
    }

    [Fact]
    public void Ticking_ignored_files_searches_them_again()
    {
        var git = new FakeGitCli { ListedFiles = ignored => ignored ? ["a.txt", ".idea/b.txt"] : ["a.txt"] };
        var (view, _) = Build(git, ("/work/a.txt", "needle"), ("/work/.idea/b.txt", "needle"));
        view.Query = "needle";

        view.ToggleIgnoredFiles();

        Assert.True(view.IncludeIgnored);
        Assert.Equal("2 in 2 files", view.StatusText);
    }

    [Fact]
    public void Without_git_installed_the_fixed_skip_list_applies_and_ignored_files_is_disabled()
    {
        var (view, _) = Build(new FakeGitCli { Missing = true }, ("/work/a.txt", "needle"), ("/work/obj/b.txt", "needle"));

        view.Query = "needle";

        Assert.Equal("1 in 1 file (not a git repo)", view.StatusText);
        Assert.False(view.IgnoredFilesAvailable);
    }

    [Fact]
    public void Toggling_match_case_or_whole_word_searches_again()
    {
        var (view, _) = Build(("/work/a.cs", "Id id Hidden"));
        view.Query = "id";
        Assert.Equal(3, view.Result.MatchCount);

        view.ToggleMatchCase();
        Assert.Equal(2, view.Result.MatchCount);

        view.ToggleWholeWord();
        Assert.Equal(1, view.Result.MatchCount);
        Assert.Equal(new MatchOptions(MatchCase: true, WholeWord: true), view.Options);
    }

    [Fact]
    public void ReplaceAll_honours_match_case_and_whole_word()
    {
        var (view, fs) = Build(("/work/a.cs", "Id id Hidden.Id"));
        view.Query = "Id";
        view.Replacement = "Key";
        view.ToggleMatchCase();
        view.ToggleWholeWord();

        view.RequestReplaceAll();
        view.RequestReplaceAll();

        Assert.Equal("Key id Hidden.Key", fs.File.ReadAllText("/work/a.cs"));
    }

    [Fact]
    public void Toggling_an_option_disarms_a_pending_replace_all()
    {
        var (view, fs) = Build(("/work/a.cs", "Id id"));
        view.Query = "Id";
        view.Replacement = "Key";
        view.RequestReplaceAll();

        view.ToggleMatchCase();
        view.RequestReplaceAll();

        Assert.Equal("Id id", fs.File.ReadAllText("/work/a.cs"));
    }

    [Fact]
    public void Query_without_an_open_folder_says_so()
    {
        using var view = new SearchView();

        view.Query = "x";

        Assert.Equal("No folder open", view.StatusText);
    }

    [Fact]
    public void Activating_a_match_raises_MatchActivated_with_its_position()
    {
        var (view, fs) = Build(("/work/a.txt", "one\ntwo needle"));
        view.Query = "needle";
        (IFileInfo File, TuiCode.Abstractions.TextMatch Match)? hit = null;
        view.MatchActivated += (_, e) => hit = e;

        var fileNode = Assert.IsType<FileNode>(Assert.Single(view.Results.Objects!));
        view.Results.SelectedObject = fileNode.Children[0];
        view.Results.NewKeyDownEvent(Key.Enter);

        Assert.NotNull(hit);
        Assert.Equal(fs.Path.GetFullPath("/work/a.txt"), hit.Value.File.FullName);
        Assert.Equal(new TuiCode.Abstractions.TextMatch(1, 4, 6), hit.Value.Match);
    }

    [Fact]
    public void ReplaceAll_needs_a_second_request_to_confirm()
    {
        var (view, fs) = Build(("/work/a.txt", "foo"), ("/work/b.txt", "foo foo"));
        view.Query = "foo";
        view.Replacement = "bar";

        view.RequestReplaceAll();
        Assert.Equal("foo", fs.File.ReadAllText("/work/a.txt"));

        view.RequestReplaceAll();
        Assert.Equal("bar", fs.File.ReadAllText("/work/a.txt"));
        Assert.Equal("bar bar", fs.File.ReadAllText("/work/b.txt"));
        Assert.Equal(0, view.Result.MatchCount);
    }

    [Theory]
    [InlineData("needle needle", "2 in 1 file")]
    [InlineData("needle", "1 in 1 file")]
    public void Matches_in_one_file_count_it_as_one_file(string content, string expected)
    {
        var (view, _) = Build(new FakeGitCli { ListedFiles = _ => ["a.txt"] }, ("/work/a.txt", content));

        view.Query = "needle";

        Assert.Equal(expected, view.StatusText);
    }

    [Fact]
    public void A_search_cut_short_in_one_file_counts_it_as_one_file()
    {
        var (view, _) = Build(new FakeGitCli { ListedFiles = _ => ["a.txt"] }, ("/work/a.txt", new string('x', WorkspaceSearch.DefaultMaxMatches + 1)));

        view.Query = "x";

        Assert.Equal($"{WorkspaceSearch.DefaultMaxMatches}+ in 1 file", view.StatusText);
    }

    [Theory]
    [InlineData(new[] { "foo" },
        "Replace 1 match in 1 file with 'bar'? Press Ctrl+Enter again to confirm.", "Replaced 1 occurrence in 1 file")]
    [InlineData(new[] { "foo", "foo foo" },
        "Replace 3 matches in 2 files with 'bar'? Press Ctrl+Enter again to confirm.", "Replaced 3 occurrences in 2 files")]
    public void ReplaceAll_messages_count_matches_and_files(string[] contents, string confirm, string replaced)
    {
        var (view, _) = Build(new FakeGitCli { ListedFiles = _ => [.. contents.Select((_, i) => $"{i}.txt")] },
            [.. contents.Select((content, i) => ($"/work/{i}.txt", content))]);
        var messages = new List<string>();
        view.Message += (_, message) => messages.Add(message);
        view.Query = "foo";
        view.Replacement = "bar";

        view.RequestReplaceAll();
        view.RequestReplaceAll();

        Assert.Equal([confirm, replaced], messages);
    }

    [Fact]
    public void Changing_the_replacement_disarms_a_pending_replace_all()
    {
        var (view, fs) = Build(("/work/a.txt", "foo"));
        view.Query = "foo";
        view.Replacement = "bar";
        view.RequestReplaceAll();

        view.Replacement = "baz";
        view.RequestReplaceAll();

        Assert.Equal("foo", fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public void ShowReplace_reveals_the_replace_input()
    {
        using var view = new SearchView();
        Assert.False(view.ReplaceVisible);

        view.ShowReplace(true);

        Assert.True(view.ReplaceVisible);
    }

    private static (SearchView View, MockFileSystem Fs) Build(params (string Path, string Content)[] files) => Build(null, files);

    private static (SearchView View, MockFileSystem Fs) Build(FakeGitCli? git, params (string Path, string Content)[] files)
    {
        var fs = new MockFileSystem();
        foreach (var (path, content) in files)
            fs.AddFile(path, new MockFileData(content));
        fs.AddDirectory("/work");
        var root = fs.DirectoryInfo.New("/work");
        return (new SearchView { RootProvider = () => root, Git = git, InRepository = _ => git is not null }, fs);
    }
}
