using TuiCode.Syntax;
using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class SymbolScanTests
{
    private static readonly GrammarBundle Bundle = GrammarBundle.Load();

    // Compiling a cold grammar's regexes can outrun the production per-line limit, which would have the scan
    // re-read the line and report itself unfinished. These fixtures are about what it finds, not about that.
    private static readonly TimeSpan Patient = TimeSpan.FromMinutes(1);

    private readonly SyntaxHighlighter _highlighter = new(Bundle);

    private const string CSharp = """
        public class Widget
        {
            public int Count { get; set; }
            private readonly string _name;
            public async Task<int> DoWorkAsync(int x)
            {
                Console.WriteLine("hi");
                await DoWorkAsync(1);
                var y = Compute(x);
                return y;
            }
            public string Name => _name;
        }
        public interface IThing { }
        public enum Colour { Red }
        public struct Point { }
        """;

    [Fact]
    public void Csharp_finds_types_methods_and_properties_in_file_order()
    {
        Assert.Equal(
            [
                new FileSymbol("Widget", SymbolKind.Type, 0),
                new FileSymbol("Count", SymbolKind.Property, 2),
                new FileSymbol("DoWorkAsync", SymbolKind.Method, 4),
                new FileSymbol("Name", SymbolKind.Property, 11),
                new FileSymbol("IThing", SymbolKind.Type, 13),
                new FileSymbol("Colour", SymbolKind.Type, 14),
                new FileSymbol("Point", SymbolKind.Type, 15),
            ],
            Scan("csharp", CSharp));
    }

    [Fact]
    public void A_call_is_not_a_symbol_and_a_return_type_is_not_a_type()
    {
        var symbols = Scan("csharp", CSharp);

        Assert.Equal(new FileSymbol("DoWorkAsync", SymbolKind.Method, 4), Assert.Single(symbols, s => s.Name == "DoWorkAsync"));
        Assert.DoesNotContain(symbols, s => s.Name is "WriteLine" or "Console" or "Compute");
        Assert.DoesNotContain(symbols, s => s.Name == "Task");
    }

    [Fact]
    public void Csharp_finds_a_constructor_and_a_local_function_but_not_the_calls_around_them()
    {
        var symbols = Scan("csharp", """
            public class Widget
            {
                public Widget(int x) { }
                void Run()
                {
                    int Local(int a) => a;
                    Widget w = Build();
                    if (TryGet(out var v)) { }
                    Foo();
                }
            }
            """);

        Assert.Equal(
            ["Widget", "Widget", "Run", "Local"],
            symbols.Select(s => s.Name));
    }

    [Fact]
    public void Typescript_finds_classes_interfaces_enums_methods_and_fields()
    {
        Assert.Equal(
            [
                new FileSymbol("Widget", SymbolKind.Type, 0),
                new FileSymbol("name", SymbolKind.Property, 1),
                new FileSymbol("label", SymbolKind.Method, 2),
                new FileSymbol("doWork", SymbolKind.Method, 3),
                new FileSymbol("Thing", SymbolKind.Type, 8),
                new FileSymbol("a", SymbolKind.Property, 8),
                new FileSymbol("Colour", SymbolKind.Type, 9),
                new FileSymbol("topLevel", SymbolKind.Method, 10),
            ],
            Scan("typescript", """
                export class Widget {
                  private name: string;
                  get label(): string { return this.name; }
                  async doWork(x: number): Promise<number> {
                    console.log('hi');
                    return this.doWork(1);
                  }
                }
                export interface Thing { a: number }
                export enum Colour { Red }
                function topLevel(a: string) { return a; }
                """));
    }

    [Fact]
    public void Python_finds_the_class_and_every_def_but_no_calls()
    {
        Assert.Equal(
            [
                new FileSymbol("Widget", SymbolKind.Type, 0),
                new FileSymbol("__init__", SymbolKind.Method, 1),
                new FileSymbol("do_work", SymbolKind.Method, 3),
                new FileSymbol("label", SymbolKind.Method, 7),
                new FileSymbol("top_level", SymbolKind.Method, 9),
            ],
            Scan("python", """
                class Widget:
                    def __init__(self, name):
                        self.name = name
                    def do_work(self, x):
                        print('hi')
                        return self.do_work(1)
                    @property
                    def label(self):
                        return self.name
                def top_level(a):
                    return a
                """));
    }

    [Fact]
    public void Rust_finds_structs_traits_enums_and_fn_items_but_not_i32_or_str()
    {
        var symbols = Scan("rust", """
            pub struct Widget {
                name: String,
            }
            pub trait Thing {
                fn describe(&self) -> String;
            }
            impl Widget {
                pub fn do_work(&self, x: i32) -> i32 {
                    println!("hi");
                    self.do_work(1)
                }
            }
            pub enum Colour { Red }
            fn top_level(a: &str) -> i32 { 0 }
            """);

        Assert.Equal(
            [
                new FileSymbol("Widget", SymbolKind.Type, 0),
                new FileSymbol("Thing", SymbolKind.Type, 3),
                new FileSymbol("describe", SymbolKind.Method, 4),
                new FileSymbol("do_work", SymbolKind.Method, 7),
                new FileSymbol("Colour", SymbolKind.Type, 12),
                new FileSymbol("top_level", SymbolKind.Method, 13),
            ],
            symbols);
        Assert.DoesNotContain(symbols, s => s.Name is "i32" or "str" or "String" or "println!");
    }

    [Fact]
    public void A_grammar_that_yields_no_definitions_returns_an_empty_list()
    {
        Assert.Empty(Scan("json", """
            {
              "name": "tuicode",
              "nested": { "count": 1 }
            }
            """));
    }

    [Fact]
    public void A_file_without_a_grammar_has_no_scan_at_all()
    {
        Assert.Null(_highlighter.CreateSymbolScan(_highlighter.LanguageForFile("notes.unknown"), ["anything"]));
    }

    [Fact]
    public void Advance_stops_once_the_budget_is_spent_but_always_scans_a_line()
    {
        var scan = ScanFor("csharp", Enumerable.Repeat("public class A { }", 20).ToArray());
        scan.LineTimeLimit = Patient;

        Assert.False(scan.Advance(TimeSpan.Zero));

        Assert.Equal(1, scan.LinesScanned);
        Assert.False(scan.Done);
        Assert.Single(scan.Symbols);
    }

    [Fact]
    public void Advancing_to_the_end_leaves_the_scan_done_and_costs_nothing_more()
    {
        var scan = ScanFor("csharp", CSharp.Split('\n'));
        scan.LineTimeLimit = Patient;

        Assert.True(scan.Advance(TimeSpan.MaxValue));
        var scanned = scan.LinesScanned;
        Assert.True(scan.Advance(TimeSpan.MaxValue));

        Assert.True(scan.Done);
        Assert.Equal(scanned, scan.LinesScanned);
    }

    [Fact]
    public void Lines_over_the_length_limit_are_skipped_rather_than_tokenized()
    {
        var scan = ScanFor("csharp", ["public class A { }", new string('x', LineTokenCache.MaxLineLength + 1), "public class B { }"]);
        scan.LineTimeLimit = Patient;

        scan.Advance(TimeSpan.MaxValue);

        Assert.Equal(["A", "B"], scan.Symbols.Select(s => s.Name));
    }

    [Fact]
    public void A_line_that_overran_its_time_limit_is_read_again_rather_than_losing_its_symbols()
    {
        var scan = ScanFor("csharp", ["public class A { }", "public class B { }"]);
        scan.LineTimeLimit = TimeSpan.Zero;

        for (var i = 0; i < SymbolScan.MaxRetries; i++)
            Assert.False(scan.Advance(TimeSpan.MaxValue));
        scan.LineTimeLimit = TimeSpan.FromMinutes(1);

        Assert.True(scan.Advance(TimeSpan.MaxValue));
        Assert.Equal(["A", "B"], scan.Symbols.Select(s => s.Name));
    }

    [Theory]
    [InlineData("dwa", new[] { "DoWorkAsync", "DoWarnAboutStaleCache" })]
    [InlineData("stale", new[] { "DoWarnAboutStaleCache" })]
    [InlineData("", new[] { "DoWorkAsync", "DoWarnAboutStaleCache", "Count" })]
    [InlineData("zz", new string[0])]
    public void Filtering_keeps_file_order(string filter, string[] expected)
    {
        IReadOnlyList<FileSymbol> symbols =
        [
            new("DoWorkAsync", SymbolKind.Method, 3),
            new("DoWarnAboutStaleCache", SymbolKind.Method, 7),
            new("Count", SymbolKind.Property, 9),
        ];

        Assert.Equal(expected, SymbolList.Filter(symbols, filter).Select(s => s.Name));
    }

    private IReadOnlyList<FileSymbol> Scan(string language, string text)
    {
        var scan = ScanFor(language, text.Split('\n'));
        scan.LineTimeLimit = Patient;
        Assert.True(scan.Advance(TimeSpan.MaxValue));
        return scan.Symbols;
    }

    private SymbolScan ScanFor(string language, IReadOnlyList<string> lines) =>
        _highlighter.CreateSymbolScan(_highlighter.LanguageById(language), lines)!;
}
