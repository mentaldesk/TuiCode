using TuiCode.Syntax;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

public class UserGrammarTests
{
    private const string Root = "/home/.tui/grammars";

    private const string DemoGrammar = """
        { "scopeName": "source.demo", "patterns": [ { "match": "#.*", "name": "comment.line.demo" } ] }
        """;

    private readonly MockFileSystem _fs = new();

    [Fact]
    public void A_user_package_adds_a_language_with_its_associations()
    {
        AddPackage("demo", Manifest("demo", "Demo", ".demo", "source.demo", "./syntaxes/demo.tmLanguage.json"));
        _fs.AddFile($"{Root}/demo/syntaxes/demo.tmLanguage.json", new MockFileData(DemoGrammar));
        var syntax = new SyntaxHighlighter(GrammarBundle.Load(_fs, Root));

        var cache = syntax.CreateCache(syntax.LanguageForFile("notes.demo"))!;
        cache.Update(["x # note"]);
        cache.TokenizeThrough(0, TimeSpan.MaxValue);

        Assert.Equal("Demo", cache.Language.Name);
        Assert.Equal("#6A9955", ColorAt(syntax, cache.TokensFor(0)!, 2), ignoreCase: true);
        Assert.Empty(syntax.Problems);
    }

    [Fact]
    public void A_user_package_takes_precedence_over_a_bundled_grammar()
    {
        AddPackage("mycsharp", Manifest("csharp", "My C#", ".cs", "source.cs", "syntaxes/cs.json"));
        _fs.AddFile($"{Root}/mycsharp/syntaxes/cs.json", new MockFileData(DemoGrammar.Replace("source.demo", "source.cs")));

        var syntax = new SyntaxHighlighter(GrammarBundle.Load(_fs, Root));

        Assert.Equal("My C#", syntax.LanguageForFile("Program.cs")?.Name);
        Assert.Equal("My C#", syntax.LanguageById("csharp")?.Name);
    }

    [Fact]
    public void Unusable_packages_are_skipped_and_reported_without_affecting_others()
    {
        AddPackage("broken", "{ not json");
        _fs.AddDirectory($"{Root}/empty");
        AddPackage("plist", Manifest("plist", "Plist", ".plist", "source.plist", "syntaxes/plist.tmLanguage"));
        AddPackage("missing", Manifest("missing", "Missing", ".missing", "source.missing", "syntaxes/missing.json"));
        AddPackage("wrongtype", """{ "contributes": { "grammars": 42 } }""");
        AddPackage("demo", Manifest("demo", "Demo", ".demo", "source.demo", "syntaxes/demo.json"));
        _fs.AddFile($"{Root}/demo/syntaxes/demo.json", new MockFileData(DemoGrammar));

        var bundle = GrammarBundle.Load(_fs, Root);

        Assert.Equal("Demo", bundle.LanguageForFile("a.demo")?.Name);
        Assert.Null(bundle.LanguageForFile("a.plist"));
        Assert.Collection(bundle.Problems,
            p => Assert.StartsWith("broken: package.json couldn't be read", p),
            p => Assert.Equal("empty: no package.json", p),
            p => Assert.Equal("missing: syntaxes/missing.json not found", p),
            p => Assert.Equal("missing: no usable grammars", p),
            p => Assert.StartsWith("plist: syntaxes/plist.tmLanguage isn't a JSON grammar", p),
            p => Assert.Equal("plist: no usable grammars", p),
            p => Assert.StartsWith("wrongtype: package.json couldn't be read", p));
    }

    [Fact]
    public void A_missing_user_folder_is_not_a_problem()
    {
        Assert.Empty(GrammarBundle.Load(_fs, Root).Problems);
    }

    [Fact]
    public void The_grammars_pane_points_at_the_folder_or_its_first_problem()
    {
        Assert.StartsWith("More grammars: add VS Code grammar packages to ~/.tui/grammars",
            GrammarAssociationsView.UserGrammarsText(new SyntaxHighlighter(GrammarBundle.Load(_fs, Root))));

        _fs.AddDirectory($"{Root}/a");
        _fs.AddDirectory($"{Root}/b");

        Assert.Equal("~/.tui/grammars: a: no package.json (+1 more)",
            GrammarAssociationsView.UserGrammarsText(new SyntaxHighlighter(GrammarBundle.Load(_fs, Root))));
    }

    [Theory]
    [InlineData("""{ "scopeName": "source.bad", "patterns": [ { "match": "(unclosed", "name": "keyword.bad" } ] }""")]
    [InlineData("{ not json")]
    [InlineData("""{ "scopeName": "source.bad", "patterns": 42 }""")]
    [InlineData("""{ "scopeName": "source.bad", "patterns": [ { "include": "#nowhere" } ], "repository": 7 }""")]
    public void A_broken_grammar_leaves_the_file_plain_instead_of_throwing(string grammar)
    {
        AddPackage("bad", Manifest("bad", "Bad", ".bad", "source.bad", "syntaxes/bad.json"));
        _fs.AddFile($"{Root}/bad/syntaxes/bad.json", new MockFileData(grammar));
        var syntax = new SyntaxHighlighter(GrammarBundle.Load(_fs, Root));

        var cache = syntax.CreateCache(syntax.LanguageForFile("a.bad"));
        cache?.Update(["(unclosed # text"]);

        Assert.True(cache?.TokenizeThrough(0, TimeSpan.MaxValue) ?? true);
        var tokens = cache?.TokensFor(0) ?? [];
        Assert.All(Enumerable.Range(0, tokens.Length / 2),
            i => Assert.Equal(SyntaxHighlighter.DefaultForeground, SyntaxHighlighter.ForegroundOf(tokens[2 * i + 1])));
    }

    private void AddPackage(string name, string manifest) =>
        _fs.AddFile($"{Root}/{name}/package.json", new MockFileData(manifest));

    private static string Manifest(string id, string name, string extension, string scope, string path) => $$"""
        {
          "contributes": {
            "languages": [ { "id": "{{id}}", "aliases": [ "{{name}}" ], "extensions": [ "{{extension}}" ] } ],
            "grammars": [ { "language": "{{id}}", "scopeName": "{{scope}}", "path": "{{path}}" } ]
          }
        }
        """;

    private static string ColorAt(SyntaxHighlighter syntax, int[] tokens, int charIndex)
    {
        var metadata = tokens[1];
        for (var i = 0; i < tokens.Length && tokens[i] <= charIndex; i += 2)
            metadata = tokens[i + 1];
        return syntax.Colors[SyntaxHighlighter.ForegroundOf(metadata)];
    }
}
