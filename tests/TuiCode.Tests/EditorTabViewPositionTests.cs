using TuiCode.Editor;

namespace TuiCode.Tests;

// Putting the cursor and scroll back (#502). Lays tabs out through a TG driver — serialised (#77).
public class EditorTabViewPositionTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public EditorTabViewPositionTests() => _app.Driver!.SetScreenSize(60, 20);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void Restoring_puts_the_cursor_and_the_top_line_back()
    {
        using var tab = LaidOut(Tab(Lines(200)));

        tab.RestoreViewPosition(new ViewPosition(120, 3, 110, 0));
        tab.Draw();

        Assert.Equal((120, 3), (tab.CursorRow, tab.CursorColumn));
        Assert.Equal(110, tab.TopRow);
        Assert.Equal(new ViewPosition(120, 3, 110, 0), tab.ViewPosition);
    }

    [Fact]
    public void A_tab_not_yet_drawn_scrolls_when_it_first_is()
    {
        using var tab = Tab(Lines(200));

        tab.RestoreViewPosition(new ViewPosition(120, 3, 110, 0));
        Assert.Equal(new ViewPosition(120, 3, 110, 0), tab.ViewPosition);
        LaidOut(tab).Draw();

        Assert.Equal(110, tab.TopRow);
        Assert.Equal((120, 3), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void A_cursor_past_the_end_of_a_shorter_file_lands_on_its_last_line()
    {
        using var tab = LaidOut(Tab(Lines(30)));

        tab.RestoreViewPosition(new ViewPosition(120, 40, 110, 0));
        tab.Draw();

        Assert.Equal((29, "line 30".Length), (tab.CursorRow, tab.CursorColumn));
        Assert.InRange(29, tab.TopRow, tab.TopRow + tab.VisibleRows - 1);
    }

    [Fact]
    public void A_column_past_the_end_of_its_line_lands_at_the_end()
    {
        using var tab = LaidOut(Tab(Lines(10)));

        tab.RestoreViewPosition(new ViewPosition(2, 99, 0, 0));

        Assert.Equal((2, "line 3".Length), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Moving_the_cursor_before_the_first_draw_wins_over_the_remembered_scroll()
    {
        using var tab = Tab(Lines(200));
        tab.RestoreViewPosition(new ViewPosition(120, 0, 110, 0));

        tab.MoveCursor(5, 0);
        LaidOut(tab).Draw();

        Assert.Equal(5, tab.CursorRow);
        Assert.InRange(5, tab.TopRow, tab.TopRow + tab.VisibleRows - 1);
    }

    [Fact]
    public void A_wrapped_tab_puts_the_remembered_line_at_the_top()
    {
        using var tab = Tab(string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line {i} " + new string('x', 70))));
        tab.WordWrap = true;
        LaidOut(tab);

        tab.RestoreViewPosition(new ViewPosition(52, 0, 50, 0));
        tab.Draw();

        Assert.Equal(52, tab.CursorRow);
        Assert.Equal(50, tab.ViewPosition.TopLine);
    }

    private EditorTab Tab(string content)
    {
        _fs.AddFile("/work/file.txt", new MockFileData(content));
        return new EditorTab(_fs.FileInfo.New("/work/file.txt")) { App = _app, Width = 60, Height = 20 };
    }

    private static EditorTab LaidOut(EditorTab tab)
    {
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        return tab;
    }

    private static string Lines(int count) => string.Join('\n', Enumerable.Range(1, count).Select(i => $"line {i}"));
}
