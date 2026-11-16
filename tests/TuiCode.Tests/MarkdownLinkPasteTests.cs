using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiCode.Editor;
using TuiCode.Syntax;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

public class MarkdownLinkPasteUrlTests
{
    [Theory]
    [InlineData("https://example.com/a?b=1#c", "https://example.com/a?b=1#c")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("  https://example.com\n", "https://example.com")]
    [InlineData("HTTPS://example.com", "HTTPS://example.com")]
    public void Url_takes_an_http_address_trimmed(string clipboard, string expected) =>
        Assert.Equal(expected, MarkdownLinkPaste.Url(clipboard));

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("https://example.com https://example.org")]
    [InlineData("https://example.com\nhttps://example.org")]
    [InlineData("see https://example.com")]
    [InlineData("mailto:me@example.com")]
    [InlineData("file:///tmp/a.txt")]
    [InlineData("ftp://example.com")]
    [InlineData(@"C:\Users\me\a.txt")]
    [InlineData("/usr/local/bin")]
    public void Url_refuses_anything_but_one_http_address(string clipboard) =>
        Assert.Null(MarkdownLinkPaste.Url(clipboard));
}

// Uses a TG Application for its clipboard — serialised (#77).
public class MarkdownLinkPasteTests : StaticConfigurationTest
{
    private const string Url = "https://example.com/docs";
    private static readonly SyntaxHighlighter Highlighter = new(GrammarBundle.Load());
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void Pasting_a_url_over_a_selected_phrase_makes_it_a_link_with_the_caret_after_it()
    {
        var view = View("markdown", "Read the docs today.");
        view.SetCarets([Selecting(0, 9, 13)]);

        Paste(view, $" {Url}\n");

        Assert.Equal([$"Read the [docs]({Url}) today."], view.LineStrings);
        Assert.Equal([At(0, 9 + $"[docs]({Url})".Length)], view.Carets);
    }

    [Fact]
    public void One_undo_brings_the_phrase_back_without_the_link()
    {
        var view = View("markdown", "Read the docs today.");
        view.SetCarets([Selecting(0, 9, 13)]);
        Paste(view, Url);

        view.NewKeyDownEvent(Key.Z.WithCtrl);

        Assert.Equal(["Read the docs today."], view.LineStrings);
        Assert.Equal([Selecting(0, 9, 13)], view.Carets);
    }

    [Fact]
    public void A_backwards_selection_and_wide_characters_still_make_a_link_of_the_selected_text()
    {
        var view = View("markdown", "日本 docs");
        view.SetCarets([new Caret(new Point(0, 0), new Point(2, 0))]);

        Paste(view, Url);

        Assert.Equal([$"[日本]({Url}) docs"], view.LineStrings);
    }

    [Fact]
    public void Every_caret_with_a_one_line_selection_becomes_a_link_and_the_others_paste_the_url()
    {
        var view = View("markdown", "one two", "three", "four `code`", "five");
        view.SetCarets([Selecting(0, 4, 7), At(1, 5), Selecting(2, 6, 10), Selecting(3, 0, 4)]);

        Paste(view, Url);

        Assert.Equal([$"one [two]({Url})", $"three{Url}", $"four `{Url}`", $"[five]({Url})"], view.LineStrings);
        view.NewKeyDownEvent(Key.Z.WithCtrl);
        Assert.Equal(["one two", "three", "four `code`", "five"], view.LineStrings);
    }

    [Theory]
    [InlineData("see https://old.example.com now", 4, 27)]
    [InlineData("run `npm test` now", 5, 13)]
    [InlineData("run ``a ` b`` now", 6, 7)]
    [InlineData("see [the docs](https://old.example.com) now", 5, 8)]
    [InlineData("see [the docs](https://old.example.com) now", 15, 38)]
    [InlineData("see ![logo](logo.png) now", 6, 10)]
    public void A_url_replaces_the_selection_when_it_is_a_url_or_in_code_or_a_link(string line, int start, int end)
    {
        var view = View("markdown", line);
        view.SetCarets([Selecting(0, start, end)]);

        Paste(view, Url);

        Assert.Equal([line[..start] + Url + line[end..]], view.LineStrings);
    }

    [Theory]
    [InlineData("```", "```")]
    [InlineData("~~~", "~~~")]
    [InlineData("````md", "````")]
    public void A_url_replaces_the_selection_inside_a_fenced_code_block(string open, string close)
    {
        var view = View("markdown", "intro", open, "call docs", close, "after docs");
        view.SetCarets([Selecting(2, 5, 9), Selecting(4, 6, 10)]);

        Paste(view, Url);

        Assert.Equal(["intro", open, $"call {Url}", close, $"after [docs]({Url})"], view.LineStrings);
    }

    [Theory]
    [InlineData("1. Step", "    ```", "    call docs", "    ```")]
    [InlineData("Quote", "> ```", "> call docs", "> ```")]
    public void A_url_replaces_the_selection_inside_a_nested_fenced_code_block(string intro, string open, string code, string close)
    {
        var view = View("markdown", intro, open, code, close, "after docs");
        var docs = code.IndexOf("docs", StringComparison.Ordinal);
        view.SetCarets([Selecting(2, docs, docs + 4), Selecting(4, 6, 10)]);

        Paste(view, Url);

        Assert.Equal([intro, open, code[..docs] + Url, close, $"after [docs]({Url})"], view.LineStrings);
    }

    [Fact]
    public void A_shorter_fence_inside_a_block_leaves_it_open()
    {
        var view = View("markdown", "````", "```", "docs", "````", "docs");
        view.SetCarets([Selecting(2, 0, 4), Selecting(4, 0, 4)]);

        Paste(view, Url);

        Assert.Equal(["````", "```", Url, "````", $"[docs]({Url})"], view.LineStrings);
    }

    [Fact]
    public void A_url_replaces_a_selection_that_spans_lines()
    {
        var view = View("markdown", "one", "two");
        view.SetCarets([new Caret(new Point(1, 1), new Point(1, 0))]);

        Paste(view, Url);

        Assert.Equal([$"o{Url}wo"], view.LineStrings);
    }

    [Theory]
    [InlineData("mailto:me@example.com")]
    [InlineData("two words")]
    [InlineData("https://a.example https://b.example")]
    public void Anything_but_a_url_replaces_the_selection(string clipboard)
    {
        var view = View("markdown", "the docs");
        view.SetCarets([Selecting(0, 4, 8)]);

        Paste(view, clipboard);

        Assert.Equal([$"the {clipboard}"], view.LineStrings);
    }

    [Fact]
    public void A_url_with_nothing_selected_goes_in_plainly()
    {
        var view = View("markdown", "see ");
        view.SetCarets([At(0, 4)]);

        Paste(view, Url);

        Assert.Equal([$"see {Url}"], view.LineStrings);
    }

    [Fact]
    public void A_url_replaces_the_selection_outside_markdown()
    {
        var view = View("csharp", "var docs = 1;");
        view.SetCarets([Selecting(0, 4, 8)]);

        Paste(view, Url);

        Assert.Equal([$"var {Url} = 1;"], view.LineStrings);
    }

    private void Paste(CodeEditor view, string text)
    {
        _app.Clipboard!.SetClipboardData(text);
        view.NewKeyDownEvent(Key.V.WithCtrl);
    }

    private static Caret At(int row, int column) => new(new Point(column, row));

    private static Caret Selecting(int row, int from, int to) => new(new Point(to, row), new Point(from, row));

    private CodeEditor View(string language, params string[] lines)
    {
        var view = new CodeEditor
        {
            App = _app, Width = 60, Height = 10, Text = string.Join("\n", lines),
            Syntax = Highlighter.CreateCache(Highlighter.LanguageById(language)),
        };
        view.BeginInit();
        view.EndInit();
        view.Layout();
        return view;
    }
}
