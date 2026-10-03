using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Syntax;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Settings › Editor › Wrap by language (#381).
public class WrapByLanguageTests
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    [Theory]
    [InlineData("markdown", false, true)]
    [InlineData("markdown", true, true)]
    [InlineData("csharp", false, false)]
    [InlineData("csharp", true, true)]
    [InlineData(null, true, true)]
    public void Without_overrides_markdown_wraps_and_everything_else_follows_wrap_long_lines(
        string? language, bool wordWrap, bool expected)
    {
        Assert.Equal(expected, (EditorSettings.Default with { WordWrap = wordWrap }).WrapsLanguage(language));
    }

    [Fact]
    public void A_language_override_beats_the_built_in_one_and_the_global_default()
    {
        var settings = EditorSettings.Default with
        {
            WordWrap = true,
            WrapByLanguage = new Dictionary<string, bool> { ["markdown"] = false, ["csharp"] = false, ["python"] = true },
        };

        Assert.False(settings.WrapsLanguage("markdown"));
        Assert.False(settings.WrapsLanguage("csharp"));
        Assert.True(settings.WrapsLanguage("python"));
        Assert.True(settings.WrapsLanguage("json"));
    }

    [Fact]
    public void Settings_with_the_same_overrides_are_equal()
    {
        var a = EditorSettings.Default with { WrapByLanguage = new Dictionary<string, bool> { ["python"] = true } };
        var b = EditorSettings.Default with { WrapByLanguage = new Dictionary<string, bool> { ["python"] = true } };

        Assert.Equal(a, b);
        Assert.NotEqual(EditorSettings.Default, a);
    }

    [Fact]
    public void Markdown_is_listed_on_by_default()
    {
        var row = Assert.Single(Build([], ""));

        Assert.Equal(WrapLanguageKind.Default, row.Kind);
        Assert.Equal($"{"Markdown",-28}On  (default)", row.Display);
    }

    [Fact]
    public void Rows_mark_changed_and_added_languages()
    {
        var rows = Build(new() { ["markdown"] = false, ["python"] = true, ["nope"] = false }, "");

        Assert.Equal($"{"Markdown",-28}Off  (default: On)", rows.Single(r => r.Id == "markdown").Display);
        Assert.Equal($"{"Python",-28}On  (added)", rows.Single(r => r.Id == "python").Display);
        Assert.Equal("nope (unknown)", rows.Single(r => r.Id == "nope").Name);
    }

    [Fact]
    public void Typing_filters_the_list_and_offers_to_add_unlisted_languages()
    {
        var rows = Build(new() { ["python"] = true }, "pyth");

        Assert.Equal("python", rows[0].Id);
        Assert.Equal(WrapLanguageKind.Added, rows[0].Kind);
        Assert.DoesNotContain(rows, r => r.Id == "markdown");
        Assert.DoesNotContain(rows, r => r.Kind == WrapLanguageKind.Add && r.Id == "python");
    }

    [Fact]
    public void A_language_that_is_not_listed_gets_an_add_row_with_an_exact_name_first()
    {
        var rows = Build([], "C");

        Assert.Equal(new WrapLanguageRow("c", "C", true, null, WrapLanguageKind.Add), rows[0]);
        Assert.Equal("Add \"C\"…", rows[0].Display);
        Assert.Contains(rows, r => r is { Id: "csharp", Kind: WrapLanguageKind.Add });
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(true, null, false)]
    [InlineData(false, null, null)]
    [InlineData(null, true, false)]
    [InlineData(false, true, null)]
    [InlineData(true, true, false)]
    public void Enter_cycles_on_off_default_without_stopping_on_the_default_value(
        bool? current, bool? defaultWrap, bool? expected)
    {
        Assert.Equal(expected, WrapLanguageRows.Next(current, defaultWrap));
    }

    [Fact]
    public void A_markdown_file_opens_wrapped_with_wrap_long_lines_off()
    {
        using var group = new EditorGroup(Syntax);
        var fs = new MockFileSystem();
        fs.AddFile("/work/README.md", new MockFileData("# Title"));
        fs.AddFile("/work/a.cs", new MockFileData("int x;"));

        var markdown = group.OpenOrFocus(fs.FileInfo.New("/work/README.md"));
        var code = group.OpenOrFocus(fs.FileInfo.New("/work/a.cs"));

        Assert.True(markdown.WordWrap);
        Assert.False(code.WordWrap);
    }

    [Fact]
    public void A_language_set_off_stays_unwrapped_with_wrap_long_lines_on()
    {
        using var group = new EditorGroup(Syntax)
        {
            Settings = EditorSettings.Default with
            {
                WordWrap = true,
                WrapByLanguage = new Dictionary<string, bool> { ["csharp"] = false },
            },
        };
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.cs", new MockFileData("int x;"));
        fs.AddFile("/work/a.json", new MockFileData("{}"));

        Assert.False(group.OpenOrFocus(fs.FileInfo.New("/work/a.cs")).WordWrap);
        Assert.True(group.OpenOrFocus(fs.FileInfo.New("/work/a.json")).WordWrap);
    }

    [Fact]
    public void Changing_a_tabs_grammar_leaves_its_wrap_alone()
    {
        using var group = new EditorGroup(Syntax);
        var fs = new MockFileSystem();
        fs.AddFile("/work/README.md", new MockFileData("# Title"));
        var tab = group.OpenOrFocus(fs.FileInfo.New("/work/README.md"));

        tab.SetGrammar(Syntax.LanguageById("csharp"));

        Assert.True(tab.WordWrap);
    }

    [Fact]
    public void The_view_cycles_resets_and_adds_languages()
    {
        using var view = new EditorSettingsView(EditorSettings.Default, Syntax);

        view.CycleSelectedLanguage();
        Assert.False(view.Current.WrapByLanguage["markdown"]);
        view.CycleSelectedLanguage();
        Assert.Empty(view.Current.WrapByLanguage);
        view.CycleSelectedLanguage();
        view.ResetSelectedLanguage();
        Assert.Empty(view.Current.WrapByLanguage);

        view.SubViews.OfType<TextField>().Single().Text = "Python";
        view.CycleSelectedLanguage();
        Assert.True(view.Current.WrapByLanguage["python"]);
        view.ResetSelectedLanguage();
        Assert.Empty(view.Current.WrapByLanguage);
    }

    [Fact]
    public void The_view_lists_markdown_and_shows_the_hint()
    {
        using var view = new EditorSettingsView(EditorSettings.Default, Syntax);

        Assert.Equal("markdown", Assert.Single(view.LanguageRows).Id);
        Assert.Contains(view.SubViews.OfType<Label>(),
            l => l.Text == "Enter: On / Off / Default   Delete: reset   Type to add");
        Assert.Contains(view.SubViews.OfType<Label>(), l => l.Text == "Wrap by language");
    }

    private static IReadOnlyList<WrapLanguageRow> Build(Dictionary<string, bool> overrides, string filter) =>
        WrapLanguageRows.Build(EditorSettings.DefaultWrapByLanguage, overrides, Syntax.Languages, filter);
}
