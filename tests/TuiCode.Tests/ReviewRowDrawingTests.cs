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

    private string CSharp => _icons.ForFile("a.cs")!.Value.Glyph;

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

        Assert.Contains($"{Modified} {CSharp} a.cs  {Chat} 1", row);
        Assert.DoesNotContain("●", row);
        Assert.DoesNotContain(Chat, RowWith(rows, "b.cs"));
    }

    [Fact]
    public async Task A_file_whose_threads_are_all_resolved_is_marked_as_settled()
    {
        using var view = await Review(Thread("src/a.cs", resolved: true));

        var row = RowWith(Render(view), "a.cs");

        Assert.Contains($"{Modified} {CSharp} a.cs  {ChatSettled} 1", row);
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

        Assert.Contains($"{Modified} {CSharp} a.cs", Render(view)[row]);
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

        Assert.Contains($"{Deleted} {CSharp} a.cs", Render(view)[row]);
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

        Assert.Contains("A 📄 a.cs", Render(view)[row]);
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
    public async Task With_nerd_font_icons_a_file_shows_the_explorers_type_icon_between_the_mark_and_the_name()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.cs"), new GitChange(GitChangeKind.Added, "src/b.md")];
        using var view = await Review();

        var rows = Render(view);
        var row = RowIndex(view, "b.md");

        Assert.Contains($"{Modified} {CSharp} a.cs", RowWith(rows, "a.cs"));
        Assert.Contains($"{_icons.ForFile("b.md")!.Value.Glyph} b.md", rows[row]);
        Assert.Equal(IconDrawing.AttributeFor(_icons.ForFile("b.md")!.Value, CellOf(row, "b")).Foreground, CellOf(row, _icons.ForFile("b.md")!.Value.Glyph).Foreground);
    }

    [Theory]
    [InlineData(FileIconStyle.NerdFont)]
    [InlineData(FileIconStyle.Emoji)]
    public async Task A_folder_shows_the_explorers_folder_icon(FileIconStyle style)
    {
        _icons.Setting = style;
        using var view = await Review();

        Assert.Contains($"{_icons.ForDirectory(expanded: true)!.Value.Glyph} src", RowWith(Render(view), "src"));
    }

    [Fact]
    public async Task With_emoji_icons_a_file_shows_the_explorers_emoji_after_the_mark()
    {
        _icons.Setting = FileIconStyle.Emoji;
        using var view = await Review();

        Assert.Contains($"M {_icons.ForFile("a.cs")!.Value.Glyph} a.cs", RowWith(Render(view), "a.cs"));
    }

    [Fact]
    public async Task With_icons_off_a_folder_is_its_bare_path()
    {
        _icons.Setting = FileIconStyle.Off;
        using var view = await Review();

        Assert.EndsWith("-src", RowWith(Render(view), "src").TrimEnd());
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

    [Fact]
    public async Task A_files_counts_are_right_aligned_at_the_tabs_edge_and_line_up_down_the_list()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.cs"), new GitChange(GitChangeKind.Modified, "src/long.cs")];
        _git.LineCounts = new Dictionary<string, GitLineCount> { ["src/a.cs"] = new(3, 1), ["src/long.cs"] = new(120, 45) };
        using var view = await Review();

        var rows = Render(view);

        Assert.EndsWith("a.cs       +3 −1", RowWith(rows, "a.cs"));
        Assert.EndsWith("long.cs +120 −45", RowWith(rows, "long.cs"));
    }

    [Fact]
    public async Task The_added_count_takes_the_added_colour_and_the_removed_count_the_deleted_colour()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.cs")];
        _git.LineCounts = new Dictionary<string, GitLineCount> { ["src/a.cs"] = new(3, 1) };
        using var view = await Review();

        var row = RowIndex(view, "a.cs");
        var name = CellOf(row, "a");
        var (addedDark, addedLight) = FileIcons.ChangeColors(GitChangeKind.Added);
        var (deletedDark, deletedLight) = FileIcons.ChangeColors(GitChangeKind.Deleted);

        Assert.Equal(IconDrawing.AttributeFor(new FileIcon("+", addedDark, addedLight), name).Foreground, CellOf(row, "+").Foreground);
        Assert.Equal(IconDrawing.AttributeFor(new FileIcon("3", addedDark, addedLight), name).Foreground, CellOf(row, "3").Foreground);
        Assert.Equal(IconDrawing.AttributeFor(new FileIcon("−", deletedDark, deletedLight), name).Foreground, CellOf(row, "−").Foreground);
    }

    [Theory]
    [InlineData(GitChangeKind.Added, 12, 0, "+12")]
    [InlineData(GitChangeKind.Deleted, 0, 7, "−7")]
    public async Task An_added_or_deleted_file_shows_only_its_one_count(GitChangeKind kind, int added, int deleted, string expected)
    {
        _icons.Setting = FileIconStyle.Off;
        _git.Changes = [new GitChange(kind, "a.cs")];
        _git.LineCounts = new Dictionary<string, GitLineCount> { ["a.cs"] = new(added, deleted) };
        using var view = await Review();

        var row = RowWith(Render(view), "a.cs");

        Assert.EndsWith($" {expected}", row);
        Assert.DoesNotContain(expected.StartsWith('+') ? "−" : "+", row);
    }

    [Fact]
    public async Task A_pure_rename_shows_no_counts()
    {
        _icons.Setting = FileIconStyle.Off;
        _git.Changes = [new GitChange(GitChangeKind.Renamed, "b.cs", "a.cs")];
        _git.LineCounts = new Dictionary<string, GitLineCount> { ["b.cs"] = new(0, 0) };
        using var view = await Review();

        Assert.Equal("└─R b.cs", RowWith(Render(view), "b.cs").TrimEnd());
    }

    [Fact]
    public async Task A_binary_file_shows_a_faint_bin()
    {
        _icons.Setting = FileIconStyle.Off;
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.png")];
        _git.LineCounts = new Dictionary<string, GitLineCount> { ["a.png"] = new(0, 0, Binary: true) };
        using var view = await Review();

        var row = RowIndex(view, "a.png");

        Assert.EndsWith(" bin", Render(view)[row]);
        Assert.True(CellOf(row, "b").Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public async Task A_name_too_long_for_the_pane_is_cut_with_an_ellipsis_before_the_counts_are()
    {
        _icons.Setting = FileIconStyle.Off;
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a_rather_long_file_name.cs")];
        _git.LineCounts = new Dictionary<string, GitLineCount> { ["a_rather_long_file_name.cs"] = new(10, 2) };
        using var view = await Review();

        var row = RowWith(Render(view), "a_rather");

        Assert.Equal("└─M a_rather_lon… +10 −2", row);
    }

    [Fact]
    public async Task A_thread_badge_stays_after_the_name_with_the_counts_at_the_edge()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.cs")];
        _git.LineCounts = new Dictionary<string, GitLineCount> { ["src/a.cs"] = new(3, 1) };
        using var view = await Review(Thread("src/a.cs"));

        var row = RowWith(Render(view), "a.cs");

        Assert.Contains($"a.cs  {Chat} 1", row);
        Assert.EndsWith(" +3 −1", row);
    }

    [Fact]
    public async Task A_long_name_with_a_thread_badge_is_cut_and_keeps_both_the_badge_and_the_counts()
    {
        _icons.Setting = FileIconStyle.Off;
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a_rather_long_file_name.cs")];
        _git.LineCounts = new Dictionary<string, GitLineCount> { ["a_rather_long_file_name.cs"] = new(10, 2) };
        using var view = await Review(Thread("a_rather_long_file_name.cs"));

        Assert.Equal("└─M a_rathe…  ● 1 +10 −2", RowWith(Render(view), "a_rathe"));
    }

    [Fact]
    public async Task Folder_rows_show_no_counts()
    {
        _icons.Setting = FileIconStyle.Off;
        _git.LineCounts = new Dictionary<string, GitLineCount> { ["src/a.cs"] = new(3, 1), ["src/b.cs"] = new(1, 1) };
        using var view = await Review();

        Assert.EndsWith("-src", RowWith(Render(view), "src").TrimEnd());
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
