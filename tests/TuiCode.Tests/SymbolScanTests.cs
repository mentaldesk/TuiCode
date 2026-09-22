using TuiCode.Syntax;

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
                new FileSymbol("Widget", SymbolKind.Class, 0, 0),
                new FileSymbol("Count", SymbolKind.Property, 2, 1),
                new FileSymbol("_name", SymbolKind.Field, 3, 1),
                new FileSymbol("DoWorkAsync", SymbolKind.Method, 4, 1),
                new FileSymbol("Name", SymbolKind.Property, 11, 1),
                new FileSymbol("IThing", SymbolKind.Interface, 13, 0),
                new FileSymbol("Colour", SymbolKind.Enum, 14, 0),
                new FileSymbol("Red", SymbolKind.EnumMember, 14, 1),
                new FileSymbol("Point", SymbolKind.Struct, 15, 0),
            ],
            Scan("csharp", CSharp));
    }

    [Fact]
    public void Csharp_finds_every_shape_of_field_and_every_enum_member_under_its_type()
    {
        Assert.Equal(
            [
                new FileSymbol("Widget", SymbolKind.Class, 0, 0),
                new FileSymbol("Max", SymbolKind.Field, 2, 1),
                new FileSymbol("Sizes", SymbolKind.Field, 3, 1),
                new FileSymbol("_count", SymbolKind.Field, 4, 1),
                new FileSymbol("Colour", SymbolKind.Enum, 6, 0),
                new FileSymbol("Red", SymbolKind.EnumMember, 8, 1),
                new FileSymbol("Green", SymbolKind.EnumMember, 9, 1),
            ],
            Scan("csharp", """
                public class Widget
                {
                    public const int Max = 3;
                    private static readonly int[] Sizes = [1];
                    private int _count;
                }
                public enum Colour
                {
                    Red,
                    Green = 2,
                }
                """));
    }

    [Fact]
    public void A_call_is_not_a_symbol_and_a_return_type_is_not_a_type()
    {
        var symbols = Scan("csharp", CSharp);

        Assert.Equal(new FileSymbol("DoWorkAsync", SymbolKind.Method, 4, 1), Assert.Single(symbols, s => s.Name == "DoWorkAsync"));
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
    public void Typescript_tells_a_class_field_from_an_interfaces_property_and_finds_enum_members()
    {
        Assert.Equal(
            [
                new FileSymbol("Widget", SymbolKind.Class, 0, 0),
                new FileSymbol("name", SymbolKind.Field, 1, 1),
                new FileSymbol("label", SymbolKind.Method, 2, 1),
                new FileSymbol("doWork", SymbolKind.Method, 3, 1),
                new FileSymbol("Thing", SymbolKind.Interface, 8, 0),
                new FileSymbol("a", SymbolKind.Property, 8, 1),
                new FileSymbol("Colour", SymbolKind.Enum, 9, 0),
                new FileSymbol("Red", SymbolKind.EnumMember, 9, 1),
                new FileSymbol("topLevel", SymbolKind.Method, 10, 0),
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
                new FileSymbol("Widget", SymbolKind.Class, 0, 0),
                new FileSymbol("__init__", SymbolKind.Method, 1, 1),
                new FileSymbol("do_work", SymbolKind.Method, 3, 1),
                new FileSymbol("label", SymbolKind.Method, 7, 1),
                new FileSymbol("top_level", SymbolKind.Method, 9, 0),
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
                new FileSymbol("Widget", SymbolKind.Struct, 0, 0),
                new FileSymbol("Thing", SymbolKind.Trait, 3, 0),
                new FileSymbol("describe", SymbolKind.Method, 4, 1),
                new FileSymbol("do_work", SymbolKind.Method, 7, 1),
                new FileSymbol("Colour", SymbolKind.Enum, 12, 0),
                new FileSymbol("top_level", SymbolKind.Method, 13, 0),
            ],
            symbols);
        Assert.DoesNotContain(symbols, s => s.Name is "i32" or "str" or "String" or "println!");
    }

    [Fact]
    public void A_nested_type_sits_a_level_in_and_its_members_two()
    {
        Assert.Equal(
            [
                new FileSymbol("Widget", SymbolKind.Class, 0, 0),
                new FileSymbol("Count", SymbolKind.Property, 2, 1),
                new FileSymbol("Inner", SymbolKind.Class, 3, 1),
                new FileSymbol("Deep", SymbolKind.Method, 5, 2),
                new FileSymbol("Free", SymbolKind.Method, 8, 0),
            ],
            Scan("csharp", """
                public class Widget
                {
                    public int Count { get; set; }
                    private class Inner
                    {
                        public void Deep() { }
                    }
                }
                public static void Free() { }
                """));
    }

    [Fact]
    public void A_definition_on_the_same_line_as_the_one_before_it_sits_inside_it()
    {
        Assert.Equal(
            [
                new FileSymbol("Thing", SymbolKind.Interface, 0, 0),
                new FileSymbol("a", SymbolKind.Property, 0, 1),
            ],
            Scan("typescript", "export interface Thing { a: number }"));
    }

    [Fact]
    public void Markdown_headings_are_an_outline_nested_by_heading_level()
    {
        Assert.Equal(
            [
                new FileSymbol("Agent guide", SymbolKind.Heading, 0, 0),
                new FileSymbol("Audience and scope", SymbolKind.Heading, 2, 1),
                new FileSymbol("Quick start", SymbolKind.Heading, 6, 1),
                new FileSymbol("Quick start again", SymbolKind.Heading, 8, 2),
                new FileSymbol("Solution map", SymbolKind.Heading, 10, 1),
            ],
            Scan("markdown", """
                # Agent guide

                ## Audience and scope

                Some prose that isn't a heading.

                ## Quick start

                ### Quick start again

                ## Solution map
                """));
    }

    [Fact]
    public void A_heading_keeps_the_text_its_inline_markup_is_written_in()
    {
        Assert.Equal(
            ["A `code` heading and a [link](http://x)", "C# and the rest", "Closed off"],
            Scan("markdown", """
                # A `code` heading and a [link](http://x)
                ## C# and the rest
                ## Closed off ##
                """).Select(s => s.Name));
    }

    [Fact]
    public void Setext_headings_are_found_on_the_line_their_text_is_on()
    {
        Assert.Equal(
            [
                new FileSymbol("Title", SymbolKind.Heading, 0, 0),
                new FileSymbol("Sub", SymbolKind.Heading, 3, 1),
            ],
            Scan("markdown", """
                Title
                =====

                Sub
                ---
                """));
    }

    [Fact]
    public void A_hash_inside_a_fenced_code_block_is_not_a_heading()
    {
        Assert.Equal(
            ["Real heading"],
            Scan("markdown", """
                # Real heading

                ```sh
                # not a heading
                echo hi
                ```
                """).Select(s => s.Name));
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
