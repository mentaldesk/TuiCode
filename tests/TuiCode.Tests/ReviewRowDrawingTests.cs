using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Icons;
using TuiCode.Workbench.Review;

namespace TuiCode.Tests;

// The Review tab's file rows, through a TG driver — serialised (#77).
public class ReviewRowDrawingTests : StaticConfigurationTest
{
    private const int Columns = 24;
    private const string Chat = "\U000F0B79";
    private const string ChatSettled = "\U000F1414";

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();
    private readonly FileIcons _icons = new(() => new FontDetection(true, "test"));

    public ReviewRowDrawingTests()
    {
        _fs.AddDirectory("/work");
        _app.Driver!.SetScreenSize(Columns, 12);
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.cs"), new GitChange(GitChangeKind.Modified, "src/b.cs")];
        _gitHub.PullRequest = new GitHubPullRequest(186, "Threads", "main", "feature", default);
    }

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public async Task A_file_with_an_open_thread_is_marked_with_a_chat_icon_and_a_badge()
    {
        using var view = await Review(Thread("src/a.cs"), Thread("src/a.cs", resolved: true));

        var rows = Render(view);

        Assert.Contains($"{Chat} M a.cs  ● 1", RowWith(rows, "a.cs"));
        Assert.DoesNotContain(Chat, RowWith(rows, "b.cs"));
    }

    [Fact]
    public async Task A_file_whose_threads_are_all_resolved_is_marked_as_settled()
    {
        using var view = await Review(Thread("src/a.cs", resolved: true));

        Assert.Contains($"{ChatSettled} M a.cs  ○ 1", RowWith(Render(view), "a.cs"));
    }

    [Fact]
    public async Task The_icon_and_the_badge_are_bold_while_a_thread_is_open_and_faint_once_it_is_settled()
    {
        using var open = await Review(Thread("src/a.cs"));
        var row = RowIndex(open, "a.cs");

        Assert.True(StyleOf(row, Chat).HasFlag(TextStyle.Bold), "icon");
        Assert.True(StyleOf(row, "●").HasFlag(TextStyle.Bold), "badge");

        using var settled = await Review(Thread("src/a.cs", resolved: true));
        row = RowIndex(settled, "a.cs");
        Assert.True(StyleOf(row, ChatSettled).HasFlag(TextStyle.Faint), "icon");
        Assert.True(StyleOf(row, "○").HasFlag(TextStyle.Faint), "badge");
    }

    [Fact]
    public async Task With_icons_off_the_badge_still_shows()
    {
        _icons.Setting = FileIconStyle.Off;
        using var view = await Review(Thread("src/a.cs"));

        var row = RowWith(Render(view), "a.cs");

        Assert.Contains("M a.cs  ● 1", row);
        Assert.DoesNotContain(Chat, row);
    }

    private static GitHubReviewThread Thread(string path, bool resolved = false) =>
        new(path, 2, resolved, Outdated: false, [new GitHubComment("octocat", default, "Look here.")]);

    private async Task<ReviewView> Review(params GitHubReviewThread[] threads)
    {
        _gitHub.ReviewThreads = threads;
        // Built unhosted so Refresh applies the threads before the task completes, then given the app to draw through.
        var view = new ReviewView(_git, _gitHub, _icons)
        {
            RootProvider = () => _fs.DirectoryInfo.New("/work"),
            Width = Columns,
            Height = 12,
        };
        await view.Refresh();
        view.App = _app;
        view.BeginInit();
        view.EndInit();
        view.Layout();
        return view;
    }

    private static string RowWith(string[] rows, string text) => rows.First(row => row.Contains(text, StringComparison.Ordinal));

    private int RowIndex(ReviewView view, string text) => Array.FindIndex(Render(view), row => row.Contains(text, StringComparison.Ordinal));

    private TextStyle StyleOf(int row, string grapheme)
    {
        var contents = _app.Driver!.Contents!;
        var col = Enumerable.Range(0, Columns).First(c => contents[row, c].Grapheme == grapheme);
        return contents[row, col].Attribute!.Value.Style;
    }

    private string[] Render(View view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.SetNeedsDraw();
        view.Draw();
        return Enumerable.Range(0, view.Frame.Height).Select(row =>
        {
            var line = new StringBuilder();
            for (var col = 0; col < Columns; col += Math.Max(1, driver.Contents![row, col].Grapheme.GetColumns()))
                line.Append(driver.Contents![row, col].Grapheme);
            return line.ToString();
        }).ToArray();
    }
}
