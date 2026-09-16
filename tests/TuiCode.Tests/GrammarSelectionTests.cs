using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Grammars;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;
using TuiCode.Workbench.Themes;

namespace TuiCode.Tests;

public class GrammarAssociationRowsTests
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    [Fact]
    public void Rows_mark_changed_and_added_associations()
    {
        var rows = Build(new() { [".h"] = "c", [".tfvars"] = "json" }, "");

        Assert.Equal(GrammarAssociationKind.Default, rows.Single(r => r.Pattern == ".cs").Kind);
        Assert.Equal(".h                          C  (default: C++)", rows.Single(r => r.Pattern == ".h").Display);
        Assert.Equal(".tfvars                     JSON  (added)", rows.Single(r => r.Pattern == ".tfvars").Display);
    }

    [Fact]
    public void Filter_matches_the_pattern_or_the_grammar_name()
    {
        var rows = Build([], "c#");

        Assert.Contains(rows, r => r.Pattern == ".cs");
        Assert.DoesNotContain(rows, r => r.Pattern == ".py");
    }

    [Theory]
    [InlineData(".tfvars")]
    [InlineData("Tiltfile")]
    public void A_filter_that_is_a_new_pattern_offers_to_add_it_first(string pattern)
    {
        var rows = Build([], pattern);

        Assert.Equal(new GrammarAssociationRow(pattern, "", null, GrammarAssociationKind.Add), rows[0]);
    }

    [Theory]
    [InlineData(".cs")]
    [InlineData("C#")]
    [InlineData("src/a.cs")]
    [InlineData("*.cs")]
    public void No_add_row_for_an_existing_pattern_or_something_that_is_not_a_pattern(string filter)
    {
        Assert.DoesNotContain(Build([], filter), r => r.Kind == GrammarAssociationKind.Add);
    }

    [Fact]
    public void Plain_text_and_unknown_grammars_are_named()
    {
        var rows = Build(new() { [".cs"] = SyntaxHighlighter.PlainText, [".foo"] = "nope" }, "");

        Assert.Equal("Plain Text", rows.Single(r => r.Pattern == ".cs").GrammarName);
        Assert.Equal("nope (unknown)", rows.Single(r => r.Pattern == ".foo").GrammarName);
    }

    private static IReadOnlyList<GrammarAssociationRow> Build(Dictionary<string, string> associations, string filter) =>
        GrammarAssociationRows.Build(Syntax.DefaultAssociations, associations, Syntax.LanguageById, filter);
}

public class GrammarAssociationsViewTests
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    [Fact]
    public void Choosing_a_grammar_records_an_association_and_choosing_the_default_removes_it()
    {
        using var view = new GrammarAssociationsView(Syntax, new Dictionary<string, string>(), new InputScopeStack());

        view.SetAssociation(".h", Syntax.LanguageById("c"));
        Assert.Equal("c", view.CurrentAssociations[".h"]);

        view.SetAssociation(".h", Syntax.LanguageById("cpp"));
        Assert.Empty(view.CurrentAssociations);
    }

    [Fact]
    public void Choosing_plain_text_records_it_even_for_a_new_pattern()
    {
        using var view = new GrammarAssociationsView(Syntax, new Dictionary<string, string>(), new InputScopeStack());

        view.SetAssociation("package.json", null);

        Assert.Equal(SyntaxHighlighter.PlainText, view.CurrentAssociations["package.json"]);
    }
}

public class EditorTabGrammarTests
{
    private readonly SyntaxHighlighter _syntax = new(GrammarBundle.Load());

    [Fact]
    public void A_tab_takes_its_grammar_from_the_associations()
    {
        using var tab = OpenTab("/work/a.cs");

        Assert.Equal("csharp", tab.Grammar?.Id);
    }

    [Fact]
    public void A_chosen_grammar_survives_new_associations()
    {
        using var tab = OpenTab("/work/a.cs");
        var changes = 0;
        tab.GrammarChanged += (_, _) => changes++;

        tab.SetGrammar(_syntax.LanguageById("json"));
        _syntax.Associations = new Dictionary<string, string> { [".cs"] = SyntaxHighlighter.PlainText };
        tab.InferGrammar();

        Assert.Equal("json", tab.Grammar?.Id);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void InferGrammar_follows_new_associations()
    {
        using var tab = OpenTab("/work/a.cs");

        _syntax.Associations = new Dictionary<string, string> { [".cs"] = SyntaxHighlighter.PlainText };
        tab.InferGrammar();

        Assert.Null(tab.Grammar);
    }

    private EditorTab OpenTab(string path)
    {
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData("int x;"));
        return new EditorTab(fs.FileInfo.New(path), _syntax);
    }
}

// Boots a TG Application — serialised (#77).
public class GrammarHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly InMemorySettingsService _settings = new();

    [Fact]
    public void ChangeGrammar_has_a_mnemonic_but_no_default_keybinding()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var keybindings);

        Assert.Equal("cg", CommandMnemonics.For(CommandIds.ChangeGrammar));
        Assert.DoesNotContain(keybindings.Bindings, b => b.CommandId == CommandIds.ChangeGrammar);
    }

    [Fact]
    public async Task Change_grammar_recolours_the_active_tab_and_the_status_bar_shows_it()
    {
        _fs.AddFile("/work/notes.txt", new MockFileData("{ \"a\": 1 }"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        EditorTab? tab = null;
        var before = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/notes.txt")),
            () =>
            {
                tab = workbench.Editor.Group.ActiveTab;
                before = workbench.StatusBar.DisplayedText;
                _commands.TryExecute(CommandIds.ChangeGrammar);
            },
            () => workbench.SubViews.OfType<GrammarPickerView>().Any(),
            () => { foreach (var c in "json") host.App.InjectKey(new Key(c)); },
            () => { host.App.InjectKey(Key.Enter); },
            () => !workbench.SubViews.OfType<GrammarPickerView>().Any());

        Assert.Equal($"{_fs.Path.GetFullPath("/work/notes.txt")}  •  Plain Text", before);
        Assert.Equal("json", tab!.Grammar?.Id);
        Assert.EndsWith("  •  JSON", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Saving_an_association_in_settings_recolours_open_tabs()
    {
        _fs.AddFile("/work/todo.notes", new MockFileData("# heading"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/todo.notes")); },
            () => { _commands.TryExecute(CommandIds.OpenSettings); },
            () => workbench.SubViews.OfType<SettingsView>().Any(),
            () => { host.App.InjectKey(Key.CursorDown); host.App.InjectKey(Key.CursorDown); },
            () => { host.App.InjectKey(Key.CursorRight); },
            () => { foreach (var c in ".notes") host.App.InjectKey(new Key(c)); },
            () => { host.App.InjectKey(Key.Enter); },
            () => workbench.SubViews.OfType<SettingsView>().Single().SubViews.OfType<GrammarPickerView>().Any(),
            () => { foreach (var c in "markdown") host.App.InjectKey(new Key(c)); },
            () => { host.App.InjectKey(Key.Enter); },
            () => !workbench.SubViews.OfType<SettingsView>().Single().SubViews.OfType<GrammarPickerView>().Any(),
            () => { host.App.InjectKey(Key.Enter.WithCtrl); },
            () => !workbench.SubViews.OfType<SettingsView>().Any());

        Assert.Equal("markdown", _settings.GrammarAssociations[".notes"]);
        Assert.Equal("markdown", tab!.Grammar?.Id);
    }

    [Fact]
    public void Switching_theme_picks_its_token_theme()
    {
        var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var syntax = workbench.Editor.Group.Syntax!;

        _settings.Theme = BundledThemes.TurboPascal;
        Assert.Equal("turbo-pascal.json", syntax.Theme);

        _settings.Theme = BundledThemes.Daylight;
        Assert.Equal("daylight.json", syntax.Theme);
    }

    private Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(new SyntaxHighlighter(GrammarBundle.Load())), new StatusBarPart());

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out KeybindingService keybindings)
    {
        keybindings = new KeybindingService(_commands);
        return new WorkbenchHost(workbench, _commands, keybindings, new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI);
    }
}
