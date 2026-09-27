using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Editor;

namespace TuiCode.Tests;

// The sideways bars of editor and diff tabs (#296). Renders through a TG driver — serialised (#77).
public class HorizontalScrollBarTests : StaticConfigurationTest
{
    private const int Width = 31;
    private const int Height = 7;

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public HorizontalScrollBarTests() => _app.Driver!.SetScreenSize(40, 12);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    private static readonly string LongLine = new('x', 100);

    [Fact]
    public void An_editor_line_past_the_edge_shows_the_bar_sized_to_the_widest_line()
    {
        using var tab = Tab($"short\n{LongLine}");
        var bar = tab.TextView.HorizontalScrollBar;

        Assert.True(bar.Visible);
        Assert.Equal(tab.TextView.GetContentSize().Width, bar.ScrollableContentSize);
        Assert.Equal(tab.TextView.Viewport.Width, bar.VisibleContentSize);
    }

    [Fact]
    public void Short_editor_lines_show_no_bar_and_keep_every_row()
    {
        using var tab = Tab("one\ntwo");

        Assert.False(tab.TextView.HorizontalScrollBar.Visible);
        Assert.Equal(Height, tab.TextView.Viewport.Height);
    }

    [Fact]
    public void Scrolling_the_editor_right_moves_the_slider_and_the_slider_scrolls_the_text()
    {
        using var tab = Tab(LongLine);

        tab.TextView.Viewport = tab.TextView.Viewport with { X = 20 };
        Assert.Equal(20, tab.TextView.HorizontalScrollBar.Value);

        tab.TextView.HorizontalScrollBar.Value = 40;
        Assert.Equal(40, tab.TextView.Viewport.X);
    }

    [Fact]
    public void The_caret_moving_off_the_right_edge_moves_the_slider()
    {
        using var tab = Tab(LongLine);

        tab.TextView.NewKeyDownEvent(Key.End);

        Assert.NotEqual(0, tab.TextView.Viewport.X);
        Assert.Equal(tab.TextView.Viewport.X, tab.TextView.HorizontalScrollBar.Value);
    }

    [Fact]
    public void The_gutter_ends_level_with_the_last_text_row()
    {
        using var tab = Tab($"{LongLine}\n{string.Join('\n', Enumerable.Range(1, 30))}");

        Assert.True(tab.TextView.HorizontalScrollBar.Visible);
        Assert.Equal(Height - 1, tab.TextView.Viewport.Height);
        Assert.Equal(tab.TextView.Viewport.Height, Gutter(tab).Frame.Height);
    }

    [Fact]
    public void A_long_file_of_long_lines_draws_both_bars_meeting_cleanly_in_the_corner()
    {
        using var tab = Tab(string.Join('\n', Enumerable.Range(1, 30).Select(_ => LongLine)));

        var screen = Render(tab);

        Assert.True(tab.TextView.VerticalScrollBar.Visible);
        Assert.StartsWith("◄", screen[^1][Gutter(tab).Frame.Width..]);
        Assert.EndsWith("► ", screen[^1]);
        Assert.EndsWith("▼", screen[^2]);
        Assert.DoesNotContain("  7", screen[^1]);
    }

    [Fact]
    public void Widening_the_tab_until_the_line_fits_hides_the_bar_and_back()
    {
        using var tab = Tab(new string('x', 40));

        tab.Width = 80;
        tab.Layout();
        var hidden = !tab.TextView.HorizontalScrollBar.Visible;
        tab.Width = Width;
        tab.Layout();

        Assert.True(hidden);
        Assert.True(tab.TextView.HorizontalScrollBar.Visible);
    }

    [Fact]
    public void A_diff_side_with_a_line_past_its_pane_shows_one_bar_for_both()
    {
        var diff = Diff("one", $"one\n{LongLine}");

        Assert.True(diff.SidewaysBar.Visible);
        Assert.Equal(Height - 1, diff.Viewport.Height);
        Assert.Equal(Width, diff.SidewaysBar.Frame.Width);
    }

    [Fact]
    public void The_diff_slider_is_the_narrower_sides_share_of_the_widest_line()
    {
        // 31 columns: the left pane is 15, its text 10 after the gutter; the widest line is 20.
        var diff = Diff("one", $"one\n{new string('x', 20)}");

        var bar = Render(diff)[^1];

        Assert.Equal((Width - 2) * 10 / 20, bar.Count(c => c == '█'));
    }

    [Fact]
    public void A_diff_whose_lines_fit_shows_no_bar()
    {
        var diff = Diff("one", "two");

        Assert.False(diff.SidewaysBar.Visible);
        Assert.Equal(Height, diff.Viewport.Height);
    }

    [Fact]
    public void Scrolling_the_diff_sideways_moves_the_slider()
    {
        var diff = Diff("one", $"one\n{LongLine}");

        diff.NewKeyDownEvent(Key.CursorRight.WithShift);
        var paged = diff.SidewaysBar.Value;
        Assert.NotEqual(0, paged);

        diff.NewKeyDownEvent(Key.CursorLeft);
        Assert.InRange(diff.SidewaysBar.Value, 1, paged - 1);

        diff.NewMouseEvent(new Mouse { Flags = MouseFlags.WheeledRight, Position = new System.Drawing.Point(5, 1) });
        Assert.Equal(paged, diff.SidewaysBar.Value);
    }

    [Fact]
    public void Moving_the_diff_slider_scrolls_both_sides_together()
    {
        var diff = Diff($"one\nabcdefghijklmnopqrstuvwxyz", $"one\nabcdefghijklmnopqrstuvwxyZ");

        diff.SidewaysBar.Value = 4;

        Assert.NotEqual(0, diff.LeftColumn);
        var row = Render(diff)[2];
        Assert.Equal(row[5..15], row[21..]);
        Assert.StartsWith(row[5..15], "abcdefghijklmnopqrstuvwxyz"[diff.LeftColumn..]);
    }

    [Fact]
    public void The_diff_slider_reaches_its_end_where_sideways_scrolling_stops()
    {
        var diff = Diff("one", $"one\n{LongLine}");

        for (var i = 0; i < 200; i++) diff.ScrollSideways(1);
        var last = diff.LeftColumn;

        Assert.Equal(diff.SidewaysBar.ScrollableContentSize - diff.SidewaysBar.VisibleContentSize, diff.SidewaysBar.Value);
        Assert.EndsWith("►", Render(diff)[^1]);
        Assert.Equal('█', Render(diff)[^1][^2]);

        diff.SidewaysBar.Value = 0;
        diff.SidewaysBar.Value = int.MaxValue;
        Assert.Equal(last, diff.LeftColumn);
    }

    [Fact]
    public void A_long_diff_of_long_lines_draws_both_bars_meeting_cleanly_in_the_corner()
    {
        var lines = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"{i} {LongLine}"));
        var diff = Diff(lines, lines.Replace("3 x", "3 y"));

        var screen = Render(diff);

        Assert.True(diff.ScrollBar.Visible);
        Assert.True(diff.SidewaysBar.Visible);
        Assert.StartsWith("◄", screen[^1]);
        Assert.EndsWith("► ", screen[^1]);
        Assert.EndsWith("▼", screen[^2]);
    }

    private EditorTab Tab(string content)
    {
        _fs.AddFile("/work/file.txt", new MockFileData(content));
        var tab = new EditorTab(_fs.FileInfo.New("/work/file.txt")) { App = _app, Width = Width, Height = Height };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        return tab;
    }

    private static View Gutter(EditorTab tab) => tab.SubViews.First(v => v is not EditorTextView);

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
        diff.BeginInit();
        diff.EndInit();
        diff.Layout();
        diff.Refresh();
        diff.Layout();
        return diff;
    }

    private string[] Render(View view)
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
