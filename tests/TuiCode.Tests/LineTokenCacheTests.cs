using TuiCode.Syntax;

namespace TuiCode.Tests;

public class LineTokenCacheTests
{
    private const string DarkKeyword = "#569CD6";
    private const string DarkComment = "#6A9955";
    private const string LightKeyword = "#0000FF";

    private static readonly GrammarBundle Bundle = GrammarBundle.Load();

    private readonly SyntaxHighlighter _highlighter = new(Bundle);

    [Fact]
    public void CreateCache_returns_null_for_a_file_without_a_grammar()
    {
        Assert.Null(_highlighter.CreateCache(_highlighter.LanguageForFile("notes.unknown")));
    }

    [Fact]
    public void TokenizeThrough_lexes_only_down_to_the_requested_row()
    {
        var cache = CacheFor(Enumerable.Repeat("int x;", 100).ToArray());

        Assert.True(cache.TokenizeThrough(9, TimeSpan.MaxValue));

        Assert.Equal(10, cache.LinesLexed);
        Assert.NotNull(cache.TokensFor(9));
        Assert.Null(cache.TokensFor(10));
    }

    [Fact]
    public void Editing_a_line_relexes_it_and_keeps_the_tokens_of_the_lines_below()
    {
        var lines = Enumerable.Repeat("int x;", 100).ToArray();
        var cache = CacheFor(lines);
        cache.TokenizeThrough(99, TimeSpan.MaxValue);

        lines[50] = "int y;";
        cache.Update(lines);
        cache.TokenizeThrough(99, TimeSpan.MaxValue);

        Assert.Equal(101, cache.LinesLexed);
    }

    [Fact]
    public void Inserting_a_line_keeps_the_tokens_of_the_lines_below()
    {
        var lines = Enumerable.Repeat("int x;", 100).ToList();
        var cache = CacheFor(lines);
        cache.TokenizeThrough(99, TimeSpan.MaxValue);

        lines.Insert(10, "int y;");
        cache.Update(lines);
        cache.TokenizeThrough(100, TimeSpan.MaxValue);

        Assert.Equal(101, cache.LinesLexed);
        Assert.Equal(DarkKeyword, ColorAt(cache, 100, 0));
    }

    [Fact]
    public void Opening_a_block_comment_recolours_the_lines_below_it()
    {
        string[] lines = ["int a;", "int b;", "int c;"];
        var cache = CacheFor(lines);
        cache.TokenizeThrough(2, TimeSpan.MaxValue);
        Assert.Equal(DarkKeyword, ColorAt(cache, 2, 0));

        lines[0] = "/* int a;";
        cache.Update(lines);
        cache.TokenizeThrough(2, TimeSpan.MaxValue);

        Assert.Equal(DarkComment, ColorAt(cache, 2, 0));
    }

    [Fact]
    public void TokenizeThrough_stops_once_the_budget_is_spent_but_always_lexes_a_line()
    {
        var cache = CacheFor(Enumerable.Repeat("int x;", 10).ToArray());

        Assert.False(cache.TokenizeThrough(9, TimeSpan.Zero));

        Assert.Equal(1, cache.LinesLexed);
        Assert.Null(cache.TokensFor(1));
    }

    [Fact]
    public void A_line_that_overran_its_time_limit_is_relexed_on_the_next_pass()
    {
        var cache = CacheFor(["int a; // comment", "int b;"]);
        cache.LineTimeLimit = TimeSpan.Zero;
        Assert.False(cache.TokenizeThrough(1, TimeSpan.MaxValue));

        cache.LineTimeLimit = TimeSpan.FromMinutes(1);
        Assert.True(cache.TokenizeThrough(1, TimeSpan.MaxValue));

        Assert.Equal(DarkComment, ColorAt(cache, 0, 10));
        Assert.Equal(DarkKeyword, ColorAt(cache, 1, 0));
    }

    [Fact]
    public void A_line_that_always_overruns_keeps_its_partial_tokens_after_the_retries()
    {
        var cache = CacheFor(["int a; // comment"]);
        cache.LineTimeLimit = TimeSpan.Zero;

        for (var i = 0; i < LineTokenCache.MaxRetries; i++)
            Assert.False(cache.TokenizeThrough(0, TimeSpan.MaxValue));
        Assert.True(cache.TokenizeThrough(0, TimeSpan.MaxValue));
        Assert.True(cache.TokenizeThrough(0, TimeSpan.MaxValue));

        Assert.Equal(LineTokenCache.MaxRetries + 1, cache.LinesLexed);
        Assert.NotNull(cache.TokensFor(0));
    }

    [Fact]
    public void Lines_over_the_length_limit_stay_plain_and_dont_affect_the_lines_after()
    {
        string[] lines = ["/* " + new string('x', LineTokenCache.MaxLineLength), "int a;"];
        var cache = CacheFor(lines);

        cache.TokenizeThrough(1, TimeSpan.MaxValue);

        Assert.Empty(cache.TokensFor(0)!);
        Assert.Equal(DarkKeyword, ColorAt(cache, 1, 0));
    }

    [Fact]
    public void Switching_theme_invalidates_tokens_lexed_with_the_old_colours()
    {
        var cache = CacheFor(["int x;"]);
        cache.TokenizeThrough(0, TimeSpan.MaxValue);

        _highlighter.UseTheme(GrammarBundle.LightTheme);

        Assert.Null(cache.TokensFor(0));
        cache.TokenizeThrough(0, TimeSpan.MaxValue);
        Assert.Equal(LightKeyword, ColorAt(cache, 0, 0));
    }

    [Fact]
    public void Text_without_a_theme_rule_gets_the_default_foreground()
    {
        var cache = CacheFor(["int x;"]);
        cache.TokenizeThrough(0, TimeSpan.MaxValue);

        var tokens = cache.TokensFor(0)!;
        var semicolon = Enumerable.Range(0, tokens.Length / 2).Last(i => tokens[2 * i] <= 5);

        Assert.Equal(SyntaxHighlighter.DefaultForeground, SyntaxHighlighter.ForegroundOf(tokens[2 * semicolon + 1]));
    }

    [Fact]
    public void Turbo_Pascal_colours_keywords_white_and_comments_grey()
    {
        var cache = CacheFor(["int x; // note"]);
        _highlighter.UseTheme("turbo-pascal.json");

        cache.TokenizeThrough(0, TimeSpan.MaxValue);
        Assert.Equal("#FFFFFF", ColorAt(cache, 0, 0));
        Assert.Equal("#AAAAAA", ColorAt(cache, 0, 7));
    }

    private LineTokenCache CacheFor(IReadOnlyList<string> lines)
    {
        var cache = _highlighter.CreateCache(_highlighter.LanguageById("csharp"))!;
        cache.LineTimeLimit = TimeSpan.FromMinutes(1);
        cache.Update(lines);
        return cache;
    }

    private string ColorAt(LineTokenCache cache, int row, int charIndex)
    {
        var tokens = cache.TokensFor(row)!;
        var metadata = tokens[1];
        for (var i = 0; i < tokens.Length && tokens[i] <= charIndex; i += 2)
            metadata = tokens[i + 1];
        return _highlighter.Colors[SyntaxHighlighter.ForegroundOf(metadata)].ToUpperInvariant();
    }
}
