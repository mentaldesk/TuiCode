using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

// #466
public class LinksTests
{
    [Theory]
    [InlineData("https://example.com/a", 0, "https://example.com/a")]
    [InlineData("https://example.com/a", 20, "https://example.com/a")]
    [InlineData("see http://example.com here", 10, "http://example.com")]
    [InlineData("mail mailto:me@example.com now", 8, "mailto:me@example.com")]
    [InlineData("See https://example.com/a.", 10, "https://example.com/a")]
    [InlineData("See https://example.com/a, then", 10, "https://example.com/a")]
    [InlineData("Really https://example.com/a?!", 10, "https://example.com/a")]
    [InlineData("(https://example.com/a)", 5, "https://example.com/a")]
    [InlineData("(see https://example.com/a).", 8, "https://example.com/a")]
    [InlineData("https://en.wikipedia.org/wiki/Foo_(bar)", 5, "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("(https://en.wikipedia.org/wiki/Foo_(bar))", 5, "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("<https://example.com/a>", 5, "https://example.com/a")]
    [InlineData("// see https://example.com/a?q=1#top", 12, "https://example.com/a?q=1#top")]
    [InlineData("HTTPS://EXAMPLE.COM", 3, "HTTPS://EXAMPLE.COM")]
    public void At_finds_the_link_under_the_column(string line, int column, string expected) =>
        Assert.Equal(expected, Links.At(line, column));

    [Theory]
    [InlineData("Read [the docs](https://example.com) first", 7)]
    [InlineData("Read [the docs](https://example.com) first", 5)]
    [InlineData("Read [the docs](https://example.com) first", 20)]
    [InlineData("Read [the docs](https://example.com) first", 35)]
    [InlineData("Read [the docs](<https://example.com>) first", 7)]
    public void At_follows_a_markdown_link_from_its_text_or_its_url(string line, int column) =>
        Assert.Equal("https://example.com", Links.At(line, column));

    [Fact]
    public void At_a_markdown_link_with_parentheses_in_its_url_keeps_them() =>
        Assert.Equal("https://en.wikipedia.org/wiki/Foo_(bar)", Links.At("[Foo](https://en.wikipedia.org/wiki/Foo_(bar))", 2));

    [Fact]
    public void At_picks_the_link_the_column_is_on_when_a_line_has_two()
    {
        const string line = "https://one.example and https://two.example";

        Assert.Equal("https://one.example", Links.At(line, 3));
        Assert.Equal("https://two.example", Links.At(line, 30));
        Assert.Null(Links.At(line, 21));
    }

    [Theory]
    [InlineData("see https://example.com/a here", 3)]
    [InlineData("see https://example.com/a here", 25)]
    [InlineData("See https://example.com/a.", 25)]
    [InlineData("(https://example.com/a)", 0)]
    [InlineData("(https://example.com/a)", 22)]
    [InlineData("no links on this line", 4)]
    [InlineData("", 0)]
    public void At_says_nothing_just_before_or_after_a_link(string line, int column) =>
        Assert.Null(Links.At(line, column));

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/a")]
    [InlineData("example.com/a")]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[open](file:///etc/passwd)")]
    [InlineData("xhttps://example.com")]
    public void At_never_offers_another_scheme(string line)
    {
        for (var column = 0; column < line.Length; column++) Assert.Null(Links.At(line, column));
    }
}
