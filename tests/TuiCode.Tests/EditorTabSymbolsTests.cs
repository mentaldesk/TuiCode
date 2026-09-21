using TuiCode.Editor;
using TuiCode.Syntax;

namespace TuiCode.Tests;

public class EditorTabSymbolsTests
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    [Fact]
    public void The_same_scan_comes_back_until_the_buffer_changes()
    {
        using var tab = OpenTab("Widget.cs", "public class Widget { }");

        var first = tab.ScanSymbols();
        Assert.Same(first, tab.ScanSymbols());

        tab.Content = "public class Renamed { }";

        var second = tab.ScanSymbols();
        Assert.NotSame(first, second);
        Assert.Equal("Renamed", Assert.Single(Advanced(second)).Name);
    }

    [Fact]
    public void Changing_the_grammar_rescans_the_buffer()
    {
        using var tab = OpenTab("Widget.cs", "public class Widget { }");
        var first = tab.ScanSymbols();

        tab.SetGrammar(Syntax.LanguageById("typescript"));

        Assert.NotSame(first, tab.ScanSymbols());
    }

    [Fact]
    public void A_tab_without_a_grammar_has_no_scan()
    {
        using var tab = OpenTab("notes.txt", "public class Widget { }");

        Assert.Null(tab.ScanSymbols());
    }

    private static IReadOnlyList<FileSymbol> Advanced(SymbolScan? scan)
    {
        // Compiling a cold grammar's regexes can outrun the production per-line limit, which would have the
        // scan re-read the line rather than report what it found. This test is about the caching, not that.
        scan!.LineTimeLimit = TimeSpan.FromMinutes(1);
        scan.Advance(TimeSpan.MaxValue);
        return scan.Symbols;
    }

    private static EditorTab OpenTab(string name, string content)
    {
        var fs = new MockFileSystem();
        fs.AddFile($"/work/{name}", new MockFileData(content));
        return new EditorTab(fs.FileInfo.New($"/work/{name}"), Syntax);
    }
}
