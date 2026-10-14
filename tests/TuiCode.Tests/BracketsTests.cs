using TuiCode.Editor;
using TuiCode.Syntax;

namespace TuiCode.Tests;

public class BracketsTests
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    [Fact]
    public void An_opening_bracket_pairs_with_its_closer_past_nested_ones()
    {
        var brackets = Plain("if (a(b) && c)", "{", "    x = { 1 };", "}");

        Assert.Equal(Pair(0, 3, 0, 13), brackets.At(new TextSpot(0, 3)));
        Assert.Equal(Pair(1, 0, 3, 0), brackets.At(new TextSpot(1, 0)));
    }

    [Fact]
    public void A_closing_bracket_pairs_with_its_opener()
    {
        var brackets = Plain("{", "    f(x);", "}");

        Assert.Equal(Pair(2, 0, 0, 0), brackets.At(new TextSpot(2, 0)));
        Assert.Equal(Pair(1, 7, 1, 5), brackets.At(new TextSpot(1, 7)));
    }

    [Fact]
    public void Each_kind_pairs_only_with_its_own_kind()
    {
        var brackets = Plain("([)]");

        Assert.Equal(Pair(0, 0, 0, 2), brackets.At(new TextSpot(0, 0)));
        Assert.Equal(Pair(0, 1, 0, 3), brackets.At(new TextSpot(0, 1)));
    }

    [Fact]
    public void Just_after_a_bracket_it_is_the_one_chosen()
    {
        var brackets = Plain("f(x) + 1");

        Assert.Equal(Pair(0, 3, 0, 1), brackets.At(new TextSpot(0, 4)));
    }

    [Fact]
    public void Between_two_brackets_the_one_the_cursor_is_on_wins()
    {
        var brackets = Plain("(a)(b)");

        Assert.Equal(Pair(0, 3, 0, 5), brackets.At(new TextSpot(0, 3)));
    }

    [Fact]
    public void Away_from_brackets_there_is_nothing()
    {
        var brackets = Plain("(a + b)");

        Assert.Null(brackets.At(new TextSpot(0, 3)));
    }

    [Fact]
    public void A_bracket_with_no_partner_says_so()
    {
        var brackets = Plain("{", "    f(x;", "");

        Assert.Equal(new BracketPair(new TextSpot(1, 5), null), brackets.At(new TextSpot(1, 5)));
        Assert.Equal(new BracketPair(new TextSpot(0, 0), null), brackets.At(new TextSpot(0, 0)));
    }

    [Fact]
    public void Plain_text_matches_every_bracket()
    {
        var brackets = Plain("a (\")\" b");

        Assert.Equal(Pair(0, 2, 0, 4), brackets.At(new TextSpot(0, 2)));
    }

    [Fact]
    public void Brackets_in_strings_and_comments_are_ignored()
    {
        var brackets = CSharp(
            "var s = Call(\")\", ')'); // )",
            "/* ( */ var t = (1);");

        Assert.Equal(Pair(0, 12, 0, 21), brackets.At(new TextSpot(0, 12)));
        Assert.Null(brackets.At(new TextSpot(0, 14)));
        Assert.Null(brackets.At(new TextSpot(1, 3)));
        Assert.Equal(Pair(1, 16, 1, 18), brackets.At(new TextSpot(1, 16)));
    }

    [Fact]
    public void A_partner_past_the_search_limit_is_not_reported_missing()
    {
        var filler = new string('x', Brackets.SearchLimit);
        var brackets = Plain("(" + filler + ")");

        var pair = brackets.At(new TextSpot(0, 0));

        Assert.Equal(new BracketPair(new TextSpot(0, 0), null, GaveUp: true), pair);
    }

    [Fact]
    public void A_row_not_lexed_yet_stops_the_search_without_calling_the_bracket_unmatched()
    {
        string[] lines = ["{", "    x;", "}"];
        var brackets = new Brackets(lines, row => row < 2 ? [] : null);

        Assert.Equal(new BracketPair(new TextSpot(0, 0), null, GaveUp: true), brackets.At(new TextSpot(0, 0)));
    }

    [Fact]
    public void Inside_a_block_the_bracket_closing_the_innermost_one_is_found()
    {
        var brackets = Plain("{", "    f(a, (b), c);", "    g();", "}");

        Assert.Equal(new TextSpot(1, 15), brackets.Closing(new TextSpot(1, 7)));
        Assert.Equal(new TextSpot(1, 15), brackets.Closing(new TextSpot(1, 13)));
        Assert.Equal(new TextSpot(3, 0), brackets.Closing(new TextSpot(2, 2)));
        Assert.Null(brackets.Closing(new TextSpot(3, 1)));
    }

    [Fact]
    public void Inside_a_block_brackets_in_strings_are_skipped()
    {
        var brackets = CSharp("f(\"}\", x);");

        Assert.Equal(new TextSpot(0, 8), brackets.Closing(new TextSpot(0, 6)));
    }

    private static BracketPair Pair(int row, int column, int partnerRow, int partnerColumn) =>
        new(new TextSpot(row, column), new TextSpot(partnerRow, partnerColumn));

    private static Brackets Plain(params string[] lines) => new(lines, _ => []);

    private static Brackets CSharp(params string[] lines)
    {
        var cache = Syntax.CreateCache(Syntax.LanguageForFile("a.cs"))!;
        cache.LineTimeLimit = TimeSpan.FromMinutes(1);
        cache.Update(lines);
        cache.TokenizeThrough(lines.Length - 1, TimeSpan.MaxValue);
        return new Brackets(lines, cache.TokensFor);
    }
}
