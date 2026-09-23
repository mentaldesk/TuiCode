using Terminal.Gui.Input;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class EditorTabCursorTests
{
    [Fact]
    public void MoveCursor_to_middle_of_second_line_lands_at_expected_position()
    {
        using var tab = OpenTab("alpha\nbravo\ncharlie\n");

        tab.MoveCursor(1, 3);

        Assert.Equal(1, tab.CursorRow);
        Assert.Equal(3, tab.CursorColumn);
    }

    [Fact]
    public void MoveCursor_clamps_row_past_eof_to_last_line()
    {
        using var tab = OpenTab("a\nbb\nccc");

        tab.MoveCursor(99, 0);

        Assert.Equal(2, tab.CursorRow); // last line index
    }

    [Fact]
    public void MoveCursor_clamps_column_past_eol_to_line_length()
    {
        using var tab = OpenTab("alpha\nbravo");

        tab.MoveCursor(0, 99);

        Assert.Equal(0, tab.CursorRow);
        Assert.Equal(5, tab.CursorColumn);
    }

    [Fact]
    public void MoveCursor_in_an_empty_file_stays_at_the_start()
    {
        using var tab = OpenTab("");

        tab.MoveCursor(3, 4);

        Assert.Equal((0, 0), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void MoveCursor_does_not_mark_dirty()
    {
        using var tab = OpenTab("alpha\nbravo\n");
        Assert.False(tab.IsDirty);

        tab.MoveCursor(1, 2);

        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void An_edit_at_several_cursors_reports_only_where_the_primary_cursor_moves()
    {
        using var tab = OpenTab("alpha\nbravo\ncharlie\n");
        tab.MoveCursor(0, 1);
        tab.AddCursor(LineDirection.Down);
        tab.AddCursor(LineDirection.Down);
        var moves = new List<(int Row, int Column)>();
        tab.CursorMoved += (_, position) => moves.Add(position);

        tab.SubViews.OfType<EditorTextView>().Single().NewKeyDownEvent(Key.X);

        Assert.NotEmpty(moves);
        Assert.All(moves, move => Assert.Equal((0, 2), move));
    }

    [Theory]
    [InlineData(50, 45)]
    [InlineData(2, 0)]
    [InlineData(99, 90)]
    public void CenterOnCursor_puts_the_cursors_line_in_the_middle_without_scrolling_off_either_end(int row, int top)
    {
        using var tab = SizedTab(string.Join('\n', Enumerable.Range(0, 100).Select(i => $"line {i}")), height: 10);

        tab.MoveCursor(row, 0);
        tab.CenterOnCursor();

        Assert.Equal(top, tab.TopRow);
        Assert.Equal(row, tab.CursorRow);
    }

    [Fact]
    public void CenterOnCursor_leaves_a_file_that_already_fits_at_the_top()
    {
        using var tab = SizedTab("alpha\nbravo\ncharlie\n", height: 10);

        tab.MoveCursor(2, 0);
        tab.CenterOnCursor();

        Assert.Equal(0, tab.TopRow);
    }

    private static EditorTab OpenTab(string content)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/file.txt", new MockFileData(content));
        return new EditorTab(fs.FileInfo.New("/work/file.txt"));
    }

    private static EditorTab SizedTab(string content, int height)
    {
        var tab = OpenTab(content);
        tab.Width = 40;
        tab.Height = height;
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        Assert.Equal(height, tab.VisibleRows);
        return tab;
    }
}
