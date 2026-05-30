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
    public void MoveCursor_does_not_mark_dirty()
    {
        using var tab = OpenTab("alpha\nbravo\n");
        Assert.False(tab.IsDirty);

        tab.MoveCursor(1, 2);

        Assert.False(tab.IsDirty);
    }

    private static EditorTab OpenTab(string content)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/file.txt", new MockFileData(content));
        return new EditorTab(fs.FileInfo.New("/work/file.txt"));
    }
}
