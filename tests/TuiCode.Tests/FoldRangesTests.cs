using TuiCode.Editor;
using TuiCode.Syntax;

namespace TuiCode.Tests;

public class FoldRangesTests
{
    private static readonly SyntaxLanguage CSharp = new("csharp", "C#", "source.cs", "//",
        Folding: new FoldingRules(false, @"^\s*#region\b", @"^\s*#endregion\b"));

    private static readonly SyntaxLanguage Python = new("python", "Python", "source.python", "#",
        Folding: new FoldingRules(true, @"^\s*#\s*region\b", @"^\s*#\s*endregion\b"));

    private static readonly SyntaxLanguage Markdown = new("markdown", "Markdown", "text.html.markdown",
        Folding: new FoldingRules(true));

    private static IReadOnlyList<FoldRange> Find(SyntaxLanguage? language, params string[] lines) =>
        new FoldRanges(language, 4).Find(lines);

    [Fact]
    public void A_line_encloses_the_lines_indented_deeper_than_it()
    {
        Assert.Equal([new FoldRange(0, 2, false)], Find(null, "intro", "  one", "  two", "outro"));
    }

    [Fact]
    public void A_brace_on_the_next_line_and_the_closing_line_belong_to_the_header()
    {
        string[] lines = ["class A", "{", "    void Save()", "    {", "        Write();", "    }", "}"];

        Assert.Equal([new FoldRange(0, 6, true), new FoldRange(2, 5, true)], Find(CSharp, lines));
    }

    [Fact]
    public void A_header_ending_in_a_brace_folds_through_its_closing_line()
    {
        Assert.Equal([new FoldRange(0, 2, true)], Find(CSharp, "void Save() {", "    Write();", "}", "Next();"));
    }

    [Fact]
    public void Nested_blocks_come_after_the_one_around_them()
    {
        string[] lines = ["if (a)", "{", "    if (b)", "    {", "        c();", "    }", "}"];

        Assert.Equal([0, 2], Find(CSharp, lines).Select(r => r.Start));
    }

    [Fact]
    public void Region_markers_from_the_language_configuration_are_blocks()
    {
        string[] lines = ["    #region Diagnostics", "    int a;", "    int b;", "    #endregion", "    int c;"];

        Assert.Equal([new FoldRange(0, 3, true)], Find(CSharp, lines));
    }

    [Fact]
    public void A_run_of_imports_is_a_block()
    {
        string[] lines = ["using System;", "using System.IO;", "using X = Y.Z;", "", "namespace A;"];

        Assert.Equal([new FoldRange(0, 2, false)], Find(CSharp, lines));
    }

    [Fact]
    public void A_using_statement_isnt_an_import()
    {
        Assert.Empty(Find(CSharp, "using (var a = Open())", "using var b = Open();"));
    }

    [Fact]
    public void An_offside_language_leaves_trailing_blank_lines_outside_the_block()
    {
        string[] lines = ["def a():", "    return 1", "", "", "def b():", "    return 2"];

        Assert.Equal([new FoldRange(0, 1, false), new FoldRange(4, 5, false)], Find(Python, lines));
    }

    [Fact]
    public void Plain_text_folds_an_indented_paragraph_by_indentation()
    {
        string[] lines = ["Steps:", "    first", "    second", "Done."];

        Assert.Equal([new FoldRange(0, 2, false)], Find(null, lines));
    }

    [Fact]
    public void A_markdown_heading_runs_to_the_next_heading_of_the_same_or_a_higher_level()
    {
        string[] lines = ["# Title", "", "## One", "text", "### Deeper", "more", "", "## Two", "end", "# Next", "x"];

        Assert.Equal(
            [new FoldRange(0, 8, false), new FoldRange(2, 5, false), new FoldRange(4, 5, false), new FoldRange(7, 8, false), new FoldRange(9, 10, false)],
            Find(Markdown, lines));
    }

    [Fact]
    public void A_hash_inside_a_markdown_code_fence_isnt_a_heading()
    {
        string[] lines = ["## One", "```sh", "# a comment", "```", "## Two", "x"];

        Assert.Equal([0, 4], Find(Markdown, lines).Select(r => r.Start));
    }

    [Fact]
    public void Badly_indented_input_doesnt_throw()
    {
        string[] lines = ["    }", "{", "\t\tdeep", "  ", "}", ")", "#endregion", "#region", "end"];

        var ranges = Find(CSharp, lines);

        Assert.All(ranges, r => Assert.True(r.End > r.Start));
    }

    [Fact]
    public void StartsBlock_agrees_with_Find()
    {
        string[] lines = ["using A;", "using B;", "class A", "{", "    #region R", "    void Save()", "    {", "        Write();", "    }", "    #endregion", "}"];
        var rules = new FoldRanges(CSharp, 4);

        var starts = Enumerable.Range(0, lines.Length).Where(i => rules.StartsBlock(lines, i));

        Assert.Equal(rules.Find(lines).Select(r => r.Start), starts);
    }

    [Theory]
    [InlineData(new[] { "void Save()", "{", "    Write();", "}" }, 0, 3, true, " { ⋯ }")]
    [InlineData(new[] { "void Save() {", "    Write();", "}" }, 0, 2, true, " ⋯ }")]
    [InlineData(new[] { "#region Diagnostics", "int a;", "#endregion" }, 0, 2, true, " ⋯ #endregion")]
    [InlineData(new[] { "def a():", "    return 1" }, 0, 1, false, " ⋯")]
    public void Summary_shows_the_brace_and_the_closing_line(string[] lines, int start, int end, bool showsEnd, string expected)
    {
        Assert.Equal(expected, FoldRanges.Summary(lines, start, end, showsEnd));
    }
}
