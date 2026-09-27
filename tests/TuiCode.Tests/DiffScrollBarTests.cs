using Terminal.Gui.Drawing;
using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Tests;

// One vertical bar for both panes of a diff (#295). Renders through a TG driver — serialised (#77).
public class DiffScrollBarTests : StaticConfigurationTest
{
    private const int Width = 31;
    private const int Height = 7;
    private const int PageHeight = Height - 1;

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public DiffScrollBarTests() => _app.Driver!.SetScreenSize(40, 12);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void A_diff_taller_than_the_tab_shows_the_bar_sized_to_its_rows()
    {
        var diff = Diff(Lines(30), Edited(30, 10));

        Assert.True(diff.ScrollBar.Visible);
        Assert.Equal(diff.Diff.Rows.Count, diff.ScrollBar.ScrollableContentSize);
        Assert.Equal(PageHeight, diff.ScrollBar.VisibleContentSize);
        Assert.Equal(Width - 1, diff.Viewport.Width);
    }

    [Fact]
    public void A_diff_that_fits_shows_no_bar_and_keeps_its_width()
    {
        var diff = Diff(Lines(3), Edited(3, 2));

        Assert.False(diff.ScrollBar.Visible);
        Assert.Equal(Width, diff.Viewport.Width);
    }

    [Fact]
    public void A_deleted_files_diff_gets_the_bar_too()
    {
        var diff = new DiffTab(_fs.FileInfo.New("/work/gone.txt"), "main", () => DiffTab.SplitLines(Lines(30)))
        {
            App = _app,
            Width = Width,
            Height = Height,
        };
        Init(diff);

        Assert.True(diff.ScrollBar.Visible);
        Assert.Equal(30, diff.ScrollBar.ScrollableContentSize);
    }

    [Fact]
    public void Page_down_end_and_next_change_move_the_slider_with_the_rows()
    {
        var diff = Diff(Lines(30), Edited(30, 12));

        diff.NewKeyDownEvent(Key.PageDown);
        Assert.Equal(diff.TopRow, diff.ScrollBar.Value);
        Assert.NotEqual(0, diff.TopRow);

        diff.NewKeyDownEvent(Key.End);
        Assert.Equal(diff.TopRow, diff.ScrollBar.Value);

        diff.NewKeyDownEvent(Key.Home);
        diff.NextChange();
        Assert.Equal(diff.TopRow, diff.ScrollBar.Value);
        Assert.NotEqual(0, diff.TopRow);
    }

    [Fact]
    public void Moving_the_slider_scrolls_both_panes_together()
    {
        var diff = Diff(Lines(30), Edited(30, 25));

        diff.ScrollBar.Value = 15;

        Assert.Equal(15, diff.TopRow);
        var top = Render(diff)[1];
        Assert.Contains(" 16  line 16", top[..(Width / 2)]);
        Assert.Contains(" 16  line 16", top[(Width / 2)..]);
    }

    [Fact]
    public void Expanding_a_thread_grows_the_bars_content_by_the_rows_it_added()
    {
        var diff = Diff(Lines(30), Edited(30, 2));
        diff.ShowThreads([new GitHubReviewThread("a.txt", 2, false, false,
            [new GitHubComment("octocat", DateTimeOffset.UnixEpoch, "Look"), new GitHubComment("hubot", DateTimeOffset.UnixEpoch, "Reply")])]);
        var collapsed = diff.ScrollBar.ScrollableContentSize;
        diff.NewKeyDownEvent(Key.CursorDown);
        diff.NewKeyDownEvent(Key.CursorDown);

        Assert.True(diff.ToggleThread());

        Assert.Equal(collapsed + 4, diff.ScrollBar.ScrollableContentSize);
    }

    private static string Lines(int count) =>
        string.Join('\n', Enumerable.Range(1, count).Select(i => $"line {i}"));

    private static string Edited(int count, int line) =>
        string.Join('\n', Enumerable.Range(1, count).Select(i => i == line ? $"LINE {i}" : $"line {i}"));

    private DiffTab Diff(string saved, string buffer)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        var source = new EditorTab(_fs.FileInfo.New("/work/a.txt")) { Content = buffer };
        var diff = new DiffTab(source, "saved", () => DiffTab.ReadLines(source.File))
        {
            App = _app,
            Width = Width,
            Height = Height,
        };
        Init(diff);
        return diff;
    }

    private static void Init(DiffTab diff)
    {
        diff.BeginInit();
        diff.EndInit();
        diff.Layout();
        diff.Refresh();
        diff.Layout();
    }

    private string[] Render(DiffTab view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.Layout();
        view.SetNeedsDraw();
        view.Draw();
        return Enumerable.Range(0, view.Frame.Height)
            .Select(row => string.Concat(Enumerable.Range(0, view.Frame.Width).Select(col => driver.Contents![row, col].Grapheme)))
            .ToArray();
    }
}
