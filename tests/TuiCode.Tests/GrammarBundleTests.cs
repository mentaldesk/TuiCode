using System.IO.Compression;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TuiCode.Syntax;

namespace TuiCode.Tests;

public class GrammarBundleTests
{
    private static readonly GrammarBundle Bundle = GrammarBundle.Load();

    [Theory]
    [InlineData("src/Program.cs", "csharp")]
    [InlineData("PROGRAM.CS", "csharp")]
    [InlineData("README.md", "markdown")]
    [InlineData("Dockerfile", "dockerfile")]
    [InlineData("Makefile", "makefile")]
    [InlineData("bundle.js.map", "json")]
    [InlineData("app.config.ts", "typescript")]
    public void LanguageForFile_matches_file_names_then_the_longest_known_extension(string path, string expected)
    {
        Assert.Equal(expected, Bundle.LanguageForFile(path)?.Id);
    }

    [Theory]
    [InlineData("notes.unknown")]
    [InlineData("LICENSE")]
    public void LanguageForFile_returns_null_for_unknown_files(string path)
    {
        Assert.Null(Bundle.LanguageForFile(path));
    }

    [Fact]
    public void TokenizeLine_carries_a_block_comment_across_lines()
    {
        var grammar = new Registry(Bundle).LoadGrammar(Bundle.LanguageForFile("a.cs")!.ScopeName);

        var first = grammar.TokenizeLine("int x; /* starts", null, TimeSpan.MaxValue);
        var second = grammar.TokenizeLine("still comment */ int y;", first.RuleStack, TimeSpan.MaxValue);

        Assert.Contains("comment.block.cs", second.Tokens[0].Scopes);
        Assert.DoesNotContain("comment.block.cs", second.Tokens[^1].Scopes);
    }

    [Fact]
    public void GetTheme_resolves_the_base_theme_a_theme_includes()
    {
        var registry = new Registry(Bundle);
        registry.SetTheme(Bundle.GetTheme(GrammarBundle.DarkTheme)!);
        var theme = registry.GetTheme();

        // dark_plus.json defines no comment colour; it inherits dark_vs.json's.
        var rule = theme.Match(["comment"]).First();

        Assert.Equal("#6A9955", theme.GetColor(rule.foreground), ignoreCase: true);
    }

    [Fact]
    public void Smoke_loads_every_grammar_and_theme()
    {
        var output = new StringWriter();

        var exitCode = SyntaxSmoke.Run(output);

        Assert.True(exitCode == 0, output.ToString());
    }

    [Fact]
    public void Bundle_matches_the_TextMateSharp_Grammars_package()
    {
        const string grammarPrefix = "TextMateSharp.Grammars.Resources.Grammars.";
        const string themePrefix = "TextMateSharp.Grammars.Resources.Themes.";
        var package = typeof(RegistryOptions).Assembly;
        using var archive = new ZipArchive(typeof(GrammarBundle).Assembly.GetManifestResourceStream("Grammars.zip")!);

        foreach (var entry in archive.Entries)
        {
            var resource = entry.FullName.StartsWith("themes/", StringComparison.Ordinal)
                ? themePrefix + entry.Name
                : grammarPrefix + entry.FullName["grammars/".Length..].Replace('/', '.');
            using var expected = package.GetManifestResourceStream(resource);
            Assert.True(expected is not null, $"{entry.FullName} is not in the package; rerun scripts/update-grammar-bundle.cs");
            using var actual = entry.Open();
            Assert.True(ReadAll(expected).SequenceEqual(ReadAll(actual)), $"{entry.FullName} differs from the package; rerun scripts/update-grammar-bundle.cs");
        }

        var packaged = package.GetManifestResourceNames().Count(name =>
            name.StartsWith(themePrefix, StringComparison.Ordinal)
            || name.EndsWith(".package.json", StringComparison.Ordinal)
            || (name.Contains(".syntaxes.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)));
        Assert.True(packaged == archive.Entries.Count, "The package has files the bundle lacks; rerun scripts/update-grammar-bundle.cs");
    }

    private static byte[] ReadAll(Stream stream)
    {
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
