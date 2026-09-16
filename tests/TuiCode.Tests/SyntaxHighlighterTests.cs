using TuiCode.Syntax;

namespace TuiCode.Tests;

public class SyntaxHighlighterTests
{
    private static readonly GrammarBundle Bundle = GrammarBundle.Load();

    private readonly SyntaxHighlighter _highlighter = new(Bundle);

    [Fact]
    public void LanguageForFile_uses_the_default_association_when_the_user_has_none()
    {
        Assert.Equal("csharp", _highlighter.LanguageForFile("src/Program.cs")?.Id);
    }

    [Fact]
    public void LanguageForFile_prefers_a_user_association_over_the_default()
    {
        Assert.Equal("cpp", _highlighter.LanguageForFile("include/stdio.h")?.Id);

        _highlighter.Associations = new Dictionary<string, string> { [".h"] = "c" };

        Assert.Equal("c", _highlighter.LanguageForFile("include/stdio.H")?.Id);
    }

    [Fact]
    public void A_user_association_can_name_an_exact_file()
    {
        Assert.Null(_highlighter.LanguageForFile("Tiltfile"));

        _highlighter.Associations = new Dictionary<string, string> { ["Tiltfile"] = "python" };

        Assert.Equal("python", _highlighter.LanguageForFile("deploy/Tiltfile")?.Id);
        Assert.Null(_highlighter.LanguageForFile("deploy/Tiltfile.bak"));
    }

    [Theory]
    [InlineData(SyntaxHighlighter.PlainText)]
    [InlineData("no-such-grammar")]
    public void A_plain_text_or_unknown_association_turns_highlighting_off(string grammar)
    {
        _highlighter.Associations = new Dictionary<string, string> { [".cs"] = grammar };

        Assert.Null(_highlighter.LanguageForFile("Program.cs"));
    }

    [Fact]
    public void Languages_have_unique_ids()
    {
        Assert.Equal(_highlighter.Languages.Count, _highlighter.Languages.Select(l => l.Id).Distinct().Count());
    }
}
