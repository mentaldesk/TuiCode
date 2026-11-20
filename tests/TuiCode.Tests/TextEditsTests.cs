using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class TextEditsTests
{
    [Fact]
    public void Apply_makes_several_edits_on_one_line_whatever_order_they_come_in()
    {
        var renamed = TextEdits.Apply("a = Sum(Sum(1));\n", [new(0, 8, 0, 11, "Total"), new(0, 4, 0, 7, "Total")]);

        Assert.Equal("a = Total(Total(1));\n", renamed);
    }

    [Fact]
    public void Apply_keeps_each_line_ending_as_it_was()
    {
        var renamed = TextEdits.Apply("int Sum;\r\nSum();\nreturn Sum;\r", [new(0, 4, 0, 7, "Total"), new(1, 0, 1, 3, "Total"), new(2, 7, 2, 10, "Total")]);

        Assert.Equal("int Total;\r\nTotal();\nreturn Total;\r", renamed);
    }

    [Fact]
    public void Apply_counts_columns_in_UTF16_chars()
    {
        var renamed = TextEdits.Apply("var 😀 = Sum;", [new(0, 9, 0, 12, "Total")]);

        Assert.Equal("var 😀 = Total;", renamed);
    }

    [Fact]
    public void Apply_can_span_lines()
    {
        Assert.Equal("one\nfour", TextEdits.Apply("one\ntwo\nthree\nfour", [new(1, 0, 3, 0, "")]));
    }

    [Fact]
    public void Apply_refuses_an_edit_past_the_end_of_its_line_or_the_file()
    {
        Assert.Null(TextEdits.Apply("int Sum;\n", [new(0, 4, 0, 12, "Total")]));
        Assert.Null(TextEdits.Apply("int Sum;\n", [new(4, 0, 4, 3, "Total")]));
    }

    [Fact]
    public void Apply_refuses_overlapping_edits()
    {
        Assert.Null(TextEdits.Apply("int Sum;", [new(0, 4, 0, 7, "Total"), new(0, 5, 0, 6, "x")]));
    }

    [Fact]
    public void NewStarts_follows_each_edit_past_the_ones_before_it()
    {
        var places = TextEdits.NewStarts([new(0, 8, 0, 11, "Total"), new(0, 4, 0, 7, "Total"), new(1, 0, 2, 0, "a\nb\nc\n"), new(3, 2, 3, 5, "Total")]);

        Assert.Equal([(0, 10), (0, 4), (1, 0), (5, 2)], places);
    }
}
