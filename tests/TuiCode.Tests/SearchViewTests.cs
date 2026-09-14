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
        Assert.Equal("3 in 2 files", view.StatusText);
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

    private static (SearchView View, MockFileSystem Fs) Build(params (string Path, string Content)[] files)
    {
        var fs = new MockFileSystem();
        foreach (var (path, content) in files)
            fs.AddFile(path, new MockFileData(content));
        fs.AddDirectory("/work");
        var root = fs.DirectoryInfo.New("/work");
        return (new SearchView { RootProvider = () => root }, fs);
    }
}
