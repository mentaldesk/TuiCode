using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class EditorTabFindTests
{
    [Fact]
    public void Lines_exposes_buffer_lines_without_terminators()
    {
        using var tab = OpenTab("alpha\r\nbravo\n");

        Assert.Equal(["alpha", "bravo", ""], tab.Lines);
    }

    [Fact]
    public void Select_selects_the_match_and_leaves_cursor_at_its_end()
    {
        using var tab = OpenTab("alpha\nbravo charlie\n");

        tab.Select(new TextMatch(1, 6, 7));

        Assert.Equal("charlie", tab.SelectedText);
        Assert.Equal(1, tab.CursorRow);
        Assert.Equal(13, tab.CursorColumn);
    }

    [Fact]
    public void ClearSelection_drops_the_selection()
    {
        using var tab = OpenTab("alpha bravo");
        tab.Select(new TextMatch(0, 0, 5));

        tab.ClearSelection();

        Assert.Equal(string.Empty, tab.SelectedText);
    }

    [Fact]
    public void Replace_swaps_the_match_text_and_marks_dirty()
    {
        using var tab = OpenTab("one two one\n");
        var changes = 0;
        tab.ContentChanged += (_, _) => changes++;

        tab.Replace(new TextMatch(0, 8, 3), "three");

        Assert.Equal("one two three", tab.Lines[0]);
        Assert.True(tab.IsDirty);
        Assert.True(changes > 0);
    }

    [Fact]
    public void Replace_with_empty_text_deletes_the_match()
    {
        using var tab = OpenTab("keep drop keep");

        tab.Replace(new TextMatch(0, 4, 5), "");

        Assert.Equal("keep keep", tab.Lines[0]);
    }

    [Fact]
    public void Select_maps_char_columns_to_cells_past_a_surrogate_pair()
    {
        // "😀" is two UTF-16 chars but a single editor cell.
        using var tab = OpenTab("😀 target");

        tab.Select(new TextMatch(0, 3, 6));

        Assert.Equal("target", tab.SelectedText);
    }

    private static EditorTab OpenTab(string content)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/file.txt", new MockFileData(content));
        return new EditorTab(fs.FileInfo.New("/work/file.txt"));
    }
}
