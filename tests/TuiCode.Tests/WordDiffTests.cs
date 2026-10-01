using TuiCode.Editor;

namespace TuiCode.Tests;

public class WordDiffTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("count_2", new[] { "count_2" })]
    [InlineData("Log(fmt, count);", new[] { "Log", "(", "fmt", ",", " ", "count", ")", ";" })]
    [InlineData("a  \tb", new[] { "a", "  \t", "b" })]
    [InlineData("a == b", new[] { "a", " ", "=", "=", " ", "b" })]
    [InlineData("naïve café", new[] { "naïve", " ", "café" })]
    [InlineData("x = \"日本語\"", new[] { "x", " ", "=", " ", "\"", "日本語", "\"" })]
    [InlineData("été", new[] { "été" })]
    [InlineData("a😀b", new[] { "a", "😀", "b" })]
    public void Words_splits_into_identifiers_whitespace_runs_and_single_other_characters(string line, string[] expected)
    {
        var words = WordDiff.Words(line).Select(w => line.Substring(w.Start, w.Length));

        Assert.Equal(expected, words);
    }

    [Fact]
    public void Words_starts_where_it_is_told_to()
    {
        Assert.Equal([new TextRange(4, 1), new TextRange(5, 1)], WordDiff.Words("    a;", 4));
    }

    [Theory]
    [InlineData(10, 6, 0.4)]
    [InlineData(10, 0, 1.0)]
    [InlineData(10, 10, 0.0)]
    [InlineData(0, 0, 1.0)]
    public void Similarity_is_the_share_of_characters_left_unchanged(int total, int changed, double expected)
    {
        Assert.Equal(expected, WordDiff.Similarity(total, changed), 6);
    }

    [Fact]
    public void Changes_marks_only_the_word_that_changed()
    {
        var (left, right) = WordDiff.Changes("Log(fmt, count);", "Log(fmt, total);");

        Assert.Equal([new TextRange(9, 5)], left);
        Assert.Equal([new TextRange(9, 5)], right);
    }

    [Fact]
    public void Changes_marks_two_separate_edits_on_one_line_separately()
    {
        var (left, right) = WordDiff.Changes("var alpha = beta + gamma;", "var ALPHA = beta + delta;");

        Assert.Equal([new TextRange(4, 5), new TextRange(19, 5)], left);
        Assert.Equal([new TextRange(4, 5), new TextRange(19, 5)], right);
    }

    [Fact]
    public void Changes_marks_an_insertion_at_the_start_only_on_the_side_that_has_it()
    {
        var (left, right) = WordDiff.Changes("Run(alpha, beta);", "await Run(alpha, beta);");

        Assert.Empty(left);
        Assert.Equal([new TextRange(0, 6)], right);
    }

    [Fact]
    public void Changes_marks_an_insertion_at_the_end_only_on_the_side_that_has_it()
    {
        var (left, right) = WordDiff.Changes("Run(alpha, beta)", "Run(alpha, beta);");

        Assert.Empty(left);
        Assert.Equal([new TextRange(16, 1)], right);
    }

    [Fact]
    public void Changes_marks_nothing_when_only_the_indentation_changed()
    {
        var (left, right) = WordDiff.Changes("  Run(alpha, beta);", "\t\tRun(alpha, beta);");

        Assert.Empty(left);
        Assert.Empty(right);
    }

    [Fact]
    public void Changes_leaves_indentation_out_of_a_reindented_line_with_an_edit()
    {
        var (left, right) = WordDiff.Changes("  Run(alpha);", "    Run(beta);");

        Assert.Equal([new TextRange(6, 5)], left);
        Assert.Equal([new TextRange(8, 4)], right);
    }

    [Fact]
    public void Changes_marks_a_whitespace_change_in_the_middle_of_a_line()
    {
        var (left, right) = WordDiff.Changes("a = b;", "a =  b;");

        Assert.Equal([new TextRange(3, 1)], left);
        Assert.Equal([new TextRange(3, 2)], right);
    }

    [Theory]
    [InlineData("a xy", "a uvw", true)] // 4 of 9 chars shared
    [InlineData("a xyz", "a uvw", true)] // 4 of 10: exactly 40%
    [InlineData("a xyz", "a uvwq", false)] // 4 of 11
    public void Changes_marks_nothing_on_a_line_sharing_under_40_percent(string leftLine, string rightLine, bool marked)
    {
        var (left, right) = WordDiff.Changes(leftLine, rightLine);

        Assert.Equal(marked, left.Length > 0);
        Assert.Equal(marked, right.Length > 0);
    }

    [Fact]
    public void Changes_marks_nothing_against_an_empty_line()
    {
        var (left, right) = WordDiff.Changes("", "alpha");

        Assert.Empty(left);
        Assert.Empty(right);
    }
}
