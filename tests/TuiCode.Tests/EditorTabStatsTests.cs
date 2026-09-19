using TuiCode.Abstractions;
using TuiCode.Editor;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

public class EditorTabStatsTests
{
    [Fact]
    public void CountDocument_counts_the_whole_buffer()
    {
        using var tab = OpenTab("alpha bravo\ncharlie\n");

        Assert.Equal(new DocumentStats(3, 3, 18, 17), tab.CountDocument());
    }

    [Fact]
    public void CountDocument_counts_a_crlf_file_like_an_lf_one()
    {
        using var crlf = OpenTab("alpha bravo\r\ncharlie\r\n");
        using var lf = OpenTab("alpha bravo\ncharlie\n");

        Assert.Equal(lf.CountDocument(), crlf.CountDocument());
    }

    [Fact]
    public void CountDocument_counts_characters_in_the_same_unit_as_columns()
    {
        using var tab = OpenTab("e\u0301\U0001F468\u200D\U0001F469\u200D\U0001F467\t\U0001F44D\U0001F3FD");
        tab.MoveCursor(0, int.MaxValue);

        Assert.Equal(tab.CursorColumn, tab.CountDocument().Characters);
    }

    [Fact]
    public void CountDocument_follows_edits()
    {
        using var tab = OpenTab("alpha\n");
        tab.CountDocument();

        tab.Content = "alpha bravo";

        Assert.Equal(new DocumentStats(1, 2, 11, 10), tab.CountDocument());
    }

    [Fact]
    public void CountSelection_totals_the_selections_at_every_caret()
    {
        using var tab = OpenTab("alpha bravo\ncharlie delta\necho\n");
        View(tab).SetCarets([
            new Caret(new Point(11, 0), new Point(6, 0)),
            new Caret(new Point(2, 2), new Point(8, 1)),
        ]);

        Assert.Equal(new DocumentStats(3, 3, 12, 12), tab.CountSelection());
        Assert.Equal(2, tab.CaretCount);
    }

    [Fact]
    public void CountSelection_is_null_when_no_caret_has_a_selection()
    {
        using var tab = OpenTab("alpha\nbravo\n");
        View(tab).SetCarets([new Caret(new Point(1, 0)), new Caret(new Point(2, 1), new Point(2, 1))]);

        Assert.Null(tab.CountSelection());
        Assert.Equal(2, tab.CaretCount);
    }

    [Theory]
    [InlineData(LineEnding.Auto, "a\r\nb\r\n", LineEnding.CRLF)]
    [InlineData(LineEnding.Auto, "a\nb\n", LineEnding.LF)]
    [InlineData(LineEnding.LF, "a\r\nb\r\n", LineEnding.LF)]
    [InlineData(LineEnding.CRLF, "a\nb\n", LineEnding.CRLF)]
    public void LineEnding_is_the_setting_or_else_what_was_detected(LineEnding setting, string content, LineEnding expected)
    {
        using var tab = OpenTab(content);
        tab.Settings = EditorSettings.Default with { LineEnding = setting };

        Assert.Equal(expected, tab.LineEnding);
    }

    private static EditorTextView View(EditorTab tab) => tab.SubViews.OfType<EditorTextView>().Single();

    private static EditorTab OpenTab(string content)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/file.txt", new MockFileData(content));
        return new EditorTab(fs.FileInfo.New("/work/file.txt"));
    }
}
