using System.Drawing;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class QuickInfoTests
{
    private static readonly Rectangle Editor = new(10, 5, 100, 30);

    [Fact]
    public void Lay_puts_the_code_first_then_a_blank_row_then_the_prose_wrapped()
    {
        var info = QuickInfo.Lay([new HoverPart("csharp", "void Go()"), new HoverPart(null, "One two three four five.")], 12);

        Assert.Equal(["void Go()", "", "One two", "three four", "five."], info.Rows.Select(r => r.Text));
        Assert.Equal([true, false, false, false, false], info.Rows.Select(r => r.IsCode));
        Assert.Equal("csharp", info.Blocks.Single().Language);
        Assert.Equal(10, info.Width);
    }

    [Fact]
    public void Lay_wraps_a_long_signature_and_keeps_each_rows_place_in_its_line()
    {
        var info = QuickInfo.Lay([new HoverPart("csharp", "int Add(int a, int b)")], 12);

        Assert.Equal([("int Add(int", 0), ("a, int b)", 12)], info.Rows.Select(r => (r.Text, r.Offset)));
        Assert.All(info.Rows, r => Assert.Equal((0, 0), (r.Block, r.Line)));
    }

    [Fact]
    public void Lay_wraps_prose_at_a_reading_width_unless_the_code_is_wider()
    {
        var prose = new HoverPart(null, string.Join(' ', Enumerable.Repeat("word", 40)));

        var narrow = QuickInfo.Lay([new HoverPart("csharp", "void Go()"), prose], 200);
        var wide = QuickInfo.Lay([new HoverPart("csharp", new string('x', 120)), prose], 200);

        Assert.True(narrow.Width <= QuickInfo.ProseWidth);
        Assert.Equal(120, wide.Width);
        Assert.Contains(wide.Rows, r => !r.IsCode && r.Text.Length > QuickInfo.ProseWidth);
    }

    [Fact]
    public void Lay_keeps_one_blank_row_between_paragraphs()
    {
        var info = QuickInfo.Lay([new HoverPart(null, "One.\n\n\n\nTwo.")], 40);

        Assert.Equal(["One.", "", "Two."], info.Rows.Select(r => r.Text));
    }

    [Fact]
    public void Place_puts_the_box_under_the_caret_when_it_fits()
    {
        Assert.Equal(new Rectangle(30, 9, 40, 8), QuickInfo.Place(Editor, new Point(30, 8), 40, 8));
    }

    [Fact]
    public void Place_puts_the_box_above_the_caret_when_there_is_more_room_there()
    {
        Assert.Equal(new Rectangle(30, 22, 40, 8), QuickInfo.Place(Editor, new Point(30, 30), 40, 8));
    }

    [Fact]
    public void Place_shortens_the_box_to_the_room_on_the_roomier_side()
    {
        var editor = new Rectangle(10, 5, 100, 10);

        Assert.Equal(new Rectangle(30, 10, 40, 5), QuickInfo.Place(editor, new Point(30, 9), 40, 8));
        Assert.Equal(new Rectangle(30, 5, 40, 6), QuickInfo.Place(editor, new Point(30, 11), 40, 8));
    }

    [Fact]
    public void Place_makes_the_box_at_most_twelve_rows()
    {
        Assert.Equal(QuickInfo.MaxHeight, QuickInfo.Place(Editor, new Point(30, 5), 40, 50).Height);
    }

    [Fact]
    public void Place_keeps_the_box_inside_the_editor_and_never_wider_than_it()
    {
        Assert.Equal(new Rectangle(70, 9, 40, 8), QuickInfo.Place(Editor, new Point(100, 8), 40, 8));
        Assert.Equal(new Rectangle(10, 9, 100, 8), QuickInfo.Place(Editor, new Point(30, 8), 150, 8));
    }
}
