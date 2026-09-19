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
    public void GetTheme_resolves_includes_more_than_one_level_deep()
    {
        var highlighter = new SyntaxHighlighter(Bundle);
        highlighter.UseTheme("midnight.json");
        var cache = highlighter.CreateCache(highlighter.LanguageById("csharp"))!;
        cache.Update(["// note"]);
        cache.TokenizeThrough(0, TimeSpan.MaxValue);

        // midnight.json → dark_plus.json → dark_vs.json, which is where the comment colour lives.
        var comment = SyntaxHighlighter.ForegroundOf(cache.TokensFor(0)![1]);
        Assert.Equal("#6A9955", highlighter.Colors[comment], ignoreCase: true);
    }

    [Fact]
    public void A_theme_s_editor_colours_override_the_ones_it_includes()
    {
        var highlighter = new SyntaxHighlighter(Bundle);

        highlighter.UseTheme("midnight.json");

        Assert.Equal("#E6E9EF", highlighter.EditorColors["editorCursor.foreground"]);
        Assert.Equal("#1E1E1E", highlighter.EditorColors["editor.background"]);
    }

    [Theory]
    [InlineData("midnight.json")]
    [InlineData("daylight.json")]
    [InlineData("turbo-pascal.json")]
    [InlineData("modern-borland.json")]
    public void Our_themes_colour_the_cursor_gutter_and_diff(string theme)
    {
        var highlighter = new SyntaxHighlighter(Bundle);

        highlighter.UseTheme(theme);

        foreach (var key in new[]
                 {
                     "editorCursor.foreground", "editorLineNumber.foreground", "editorLineNumber.activeForeground",
                     "editorGutter.addedBackground", "editorGutter.modifiedBackground", "editorGutter.deletedBackground",
                     "diffEditor.removedLineBackground", "diffEditor.insertedLineBackground",
                 })
            Assert.True(highlighter.EditorColors.ContainsKey(key), $"{theme} has no {key}");
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

        var packagedGrammarFiles = package.GetManifestResourceNames().Count(name =>
            name.EndsWith(".package.json", StringComparison.Ordinal)
            || (name.Contains(".syntaxes.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)));
        var bundledGrammarFiles = archive.Entries.Count(e => e.FullName.StartsWith("grammars/", StringComparison.Ordinal));
        Assert.True(packagedGrammarFiles == bundledGrammarFiles, "The package has grammars the bundle lacks; rerun scripts/update-grammar-bundle.cs");
    }

    private static byte[] ReadAll(Stream stream)
    {
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
