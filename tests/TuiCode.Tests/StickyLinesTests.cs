using TuiCode.Editor;
using TuiCode.Syntax;

namespace TuiCode.Tests;

public class StickyLinesTests
{
    private static readonly GrammarBundle Bundle = GrammarBundle.Load();
    private readonly SyntaxHighlighter _highlighter = new(Bundle);

    private const string CSharp = """
        namespace Demo;

        public sealed class Widget
        {
            private readonly int _count;
            public int Count { get; set; }

            public int Total
            {
                get => _count;
            }

            public void Run(
                int times)
            {
                for (var i = 0; i < times; i++)
                {
                    Step();
                }
            }

            private void Step() => Run(1);

            public enum Mode
            {
                Fast,
                Slow,
            }
        }
        """;

    [Fact]
    public void Enclosing_is_every_definition_whose_body_holds_the_line_closing_line_included()
    {
        var sticky = Sticky("csharp", CSharp);

        Assert.Equal([2, 12], sticky.Enclosing(17));
        Assert.Equal([2, 12], sticky.Enclosing(19));
        Assert.Equal([2], sticky.Enclosing(20));
        Assert.Equal([2, 7], sticky.Enclosing(9));
        Assert.Equal([2, 23], sticky.Enclosing(27));
        Assert.Equal([2], sticky.Enclosing(3));
        Assert.Equal([2], sticky.Enclosing(28));
        Assert.Equal([], sticky.Enclosing(2));
        Assert.Equal([], sticky.Enclosing(29));
    }

    [Fact]
    public void Fields_enum_members_one_line_properties_and_expression_bodies_never_enclose_anything()
    {
        var sticky = Sticky("csharp", CSharp);

        Assert.Equal([2], sticky.Enclosing(5));
        Assert.Equal([2], sticky.Enclosing(6));
        Assert.Equal([2], sticky.Enclosing(22));
        Assert.Equal([2, 23], sticky.Enclosing(26));
    }

    [Fact]
    public void A_markdown_heading_encloses_everything_up_to_the_next_heading_of_its_level_or_higher()
    {
        var sticky = Sticky("markdown", """
            # Title

            ## Install
            text
            ### Linux
            more
            ## Use
            # Appendix
            end
            """);

        Assert.Equal([0, 2], sticky.Enclosing(3));
        Assert.Equal([0, 2, 4], sticky.Enclosing(5));
        Assert.Equal([0], sticky.Enclosing(6));
        Assert.Equal([], sticky.Enclosing(7));
        Assert.Equal([7], sticky.Enclosing(8));
    }

    [Fact]
    public void A_plain_python_body_ends_at_the_first_line_back_at_the_definitions_indent()
    {
        var sticky = Sticky("python", """
            class Thing:
                def run(self):
                    return 1

                def stop(self):
                    pass
            x = 1
            """);

        Assert.Equal([0, 1], sticky.Enclosing(2));
        Assert.Equal([0], sticky.Enclosing(3));
        Assert.Equal([0, 4], sticky.Enclosing(5));
        Assert.Equal([], sticky.Enclosing(6));
    }

    [Fact]
    public void At_pins_the_definitions_enclosing_the_first_line_left_showing_below_them()
    {
        var sticky = Sticky("csharp", CSharp);

        Assert.Equal([2, 12], sticky.At(15, Line));
        Assert.Equal([2], sticky.At(18, Line));
        Assert.Equal([2], sticky.At(19, Line));
    }

    [Fact]
    public void At_pins_nothing_while_the_definition_is_still_on_the_row_it_would_be_pinned_to()
    {
        var sticky = Sticky("csharp", CSharp);

        Assert.Equal([], sticky.At(0, Line));
        Assert.Equal([], sticky.At(2, Line));
        Assert.Equal([2], sticky.At(3, Line));
        Assert.Equal([2], sticky.At(11, Line));
        Assert.Equal([2, 12], sticky.At(12, Line));
    }

    [Fact]
    public void Nesting_deeper_than_three_keeps_the_innermost_three()
    {
        var sticky = Sticky("csharp", """
            public class A
            {
                public class B
                {
                    public class C
                    {
                        public void D()
                        {
                            Work();
                            Work();
                            Work();
                        }
                    }
                }
            }
            """);

        Assert.Equal([0, 2, 4, 6], sticky.Enclosing(9));
        Assert.Equal([2, 4, 6], sticky.At(7, Line));
    }

    [Fact]
    public void Reveal_lands_a_line_two_rows_below_the_pinned_ones()
    {
        var sticky = Sticky("csharp", CSharp);
        int Pinned(int top) => sticky.At(top, Line).Count;

        var top = Reveal.TopRow(0, 18, 40, 17, 17, Pinned);

        Assert.Equal(13, top);
        Assert.Equal(2, Pinned(13));
    }

    [Fact]
    public void Reveal_leaves_a_line_already_clear_of_the_pinned_rows_where_it_is()
    {
        var sticky = Sticky("csharp", CSharp);

        Assert.Null(Reveal.TopRow(13, 20, 40, 17, 17, top => sticky.At(top, Line).Count));
        Assert.Equal(13, Reveal.TopRow(15, 20, 40, 17, 17, top => sticky.At(top, Line).Count));
    }

    private static int Line(int row) => row;

    private StickyLines Sticky(string language, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var scan = _highlighter.CreateSymbolScan(_highlighter.LanguageById(language), lines)!;
        scan.LineTimeLimit = TimeSpan.FromMinutes(1);
        Assert.True(scan.Advance(TimeSpan.MaxValue));
        return StickyLines.From(scan.Symbols, scan.Lines);
    }
}
