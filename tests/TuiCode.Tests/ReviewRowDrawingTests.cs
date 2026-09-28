using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Icons;
using TuiCode.Workbench.Review;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Tests;

// The Review tab's file rows, through a TG driver — serialised (#77).
public class ReviewRowDrawingTests : StaticConfigurationTest
{
    private const int Columns = 24;
    private const string Chat = "\U000F0B79";
    private const string ChatSettled = "\U000F1414";
    private const string Modified = "\ueade";
    private const string Deleted = "\ueadf";

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
    public async Task A_file_with_an_open_thread_is_marked_with_a_chat_icon_in_place_of_the_circle()
    {
        using var view = await Review(Thread("src/a.cs"), Thread("src/a.cs", resolved: true));

        var rows = Render(view);
        var row = RowWith(rows, "a.cs");

        Assert.Contains($"{Modified} a.cs  {Chat} 1", row);
        Assert.DoesNotContain("●", row);
        Assert.DoesNotContain(Chat, RowWith(rows, "b.cs"));
    }

    [Fact]
    public async Task A_file_whose_threads_are_all_resolved_is_marked_as_settled()
    {
        using var view = await Review(Thread("src/a.cs", resolved: true));

        var row = RowWith(Render(view), "a.cs");

        Assert.Contains($"{Modified} a.cs  {ChatSettled} 1", row);
        Assert.DoesNotContain("○", row);
    }

    [Fact]
    public async Task The_icon_and_the_badge_are_bold_while_a_thread_is_open_and_faint_once_it_is_settled()
    {
        using var open = await Review(Thread("src/a.cs"));
        var row = RowIndex(open, "a.cs");

        Assert.True(StyleOf(row, Chat).HasFlag(TextStyle.Bold), "icon");
        Assert.True(StyleOf(row, "1").HasFlag(TextStyle.Bold), "count");

        using var settled = await Review(Thread("src/a.cs", resolved: true));
        row = RowIndex(settled, "a.cs");
        Assert.True(StyleOf(row, ChatSettled).HasFlag(TextStyle.Faint), "icon");
        Assert.True(StyleOf(row, "1").HasFlag(TextStyle.Faint), "count");
    }

    [Fact]
    public async Task With_icons_off_the_circle_stands_in_for_the_chat_icon()
    {
        _icons.Setting = FileIconStyle.Off;
        using var view = await Review(Thread("src/a.cs"));

        var row = RowWith(Render(view), "a.cs");

        Assert.Contains("M a.cs  ● 1", row);
        Assert.DoesNotContain(Chat, row);
    }

    [Fact]
    public async Task With_nerd_font_icons_each_file_starts_with_a_diff_icon_in_its_colour_and_the_name_stays_uncoloured()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.cs")];
        using var view = await Review();

        var row = RowIndex(view, "a.cs");
        var name = CellOf(row, "a");
        var mark = CellOf(row, Modified);

        Assert.Contains($"{Modified} a.cs", Render(view)[row]);
        Assert.Equal(IconDrawing.AttributeFor(_icons.ForChange(GitChangeKind.Modified)!.Value, name).Foreground, mark.Foreground);
        Assert.NotEqual(name.Foreground, mark.Foreground);
        Assert.False(name.Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public async Task A_deleted_files_name_is_drawn_faint()
    {
        _git.Changes = [new GitChange(GitChangeKind.Deleted, "src/a.cs")];
        using var view = await Review();

        var row = RowIndex(view, "a.cs");

        Assert.Contains($"{Deleted} a.cs", Render(view)[row]);
        Assert.True(CellOf(row, "a").Style.HasFlag(TextStyle.Faint));
        Assert.True(CellOf(row, "s").Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public async Task With_emoji_icons_the_letter_carries_the_changes_colour()
    {
        _icons.Setting = FileIconStyle.Emoji;
        _git.Changes = [new GitChange(GitChangeKind.Added, "src/a.cs")];
        using var view = await Review();

        var row = RowIndex(view, "a.cs");
        var name = CellOf(row, "a");

        Assert.Contains("A a.cs", Render(view)[row]);
        Assert.Equal(IconDrawing.AttributeFor(_icons.ForChange(GitChangeKind.Added)!.Value, name).Foreground, CellOf(row, "A").Foreground);
    }

    [Fact]
    public async Task With_icons_off_a_row_is_the_plain_letter_a_space_and_the_name()
    {
        _icons.Setting = FileIconStyle.Off;
        _git.Changes = [new GitChange(GitChangeKind.Deleted, "src/a.cs")];
        using var view = await Review();

        var row = RowIndex(view, "a.cs");
        var name = CellOf(row, "a");

        Assert.Equal("  └─D a.cs", Render(view)[row].TrimEnd());
        Assert.Equal(name, CellOf(row, "D"));
        Assert.False(name.Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public async Task Changing_the_icon_setting_redraws_the_marks()
    {
        using var view = await Review();
        Render(view);

        _icons.Setting = FileIconStyle.Off;

        Assert.True(view.Files.NeedsDraw);
        Assert.Contains("M a.cs", RowWith(Render(view), "a.cs"));
    }

    [Theory]
    [InlineData(FileIconStyle.NerdFont)]
    [InlineData(FileIconStyle.Emoji)]
    [InlineData(FileIconStyle.Off)]
    public async Task Typing_a_files_first_letter_moves_the_selection_to_it(FileIconStyle style)
    {
        _icons.Setting = style;
        _git.Changes = [new GitChange(GitChangeKind.Modified, "alpha.cs"), new GitChange(GitChangeKind.Modified, "beta.cs")];
        using var view = await Review();
        Assert.Equal("alpha.cs", (view.Files.SelectedObject as ReviewFileNode)?.Name);

        view.Files.NewKeyDownEvent(Key.B);

        Assert.Equal("beta.cs", (view.Files.SelectedObject as ReviewFileNode)?.Name);
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

    private Attribute CellOf(int row, string grapheme)
    {
        var contents = _app.Driver!.Contents!;
        var col = Enumerable.Range(0, Columns).First(c => contents[row, c].Grapheme == grapheme);
        return contents[row, col].Attribute!.Value;
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
