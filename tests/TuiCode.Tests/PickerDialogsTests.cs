using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using MentalDesk.Tui.Keys;
using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Grammars;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Settings;
using TuiCode.Workbench.Workspace;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TuiCode.Tests;

// The pickers and prompts on the library's PickerDialog and AppDialog (#572). Builds TG views — serialised (#77).
public class PickerDialogsTests : StaticConfigurationTest
{
    private const string Home = "/Users/me";

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    public static TheoryData<string, string> Pickers => new()
    {
        { "recent", "Enter Open" },
        { "definitions", "Enter Go to" },
        { "grammar", "Enter Choose" },
    };

    [Theory]
    [MemberData(nameof(Pickers))]
    public void A_picker_says_Type_to_filter_and_shows_its_keys_on_two_buttons_with_no_hint_row(string kind, string pick)
    {
        using var picker = Picker(kind);

        Assert.Equal("Type to filter", Assert.IsType<PlaceholderField>(picker.FilterField).Placeholder);
        Assert.Equal([pick, "Esc Cancel"], picker.Dialog.SubViews.OfType<Button>().Select(b => b.Text.Trim()));
        Assert.DoesNotContain(picker.Dialog.SubViews, v => v is Label && v.Text.Contains("Esc"));
    }

    [Theory]
    [MemberData(nameof(Pickers))]
    public void With_no_match_the_list_says_so_and_the_pick_button_dims(string kind, string _)
    {
        using var picker = Picker(kind);

        picker.FilterField.Text = "zzzzqqq";

        Assert.True(picker.NoMatches());
        Assert.False(picker.PickButton.Enabled);
    }

    [Theory]
    [InlineData("SIDE", "sidebar-arrows")]
    [InlineData("tuicode", "sidebar-arrows,save-conflict")]
    [InlineData("~/code/key", "vault")]
    [InlineData("", "sidebar-arrows,save-conflict,vault")]
    [InlineData("nothing", "")]
    public void Recent_folders_match_the_name_or_the_parent(string filter, string expected)
    {
        using var picker = RecentPicker();

        picker.FilterField.Text = filter;

        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(), picker.VisibleItems.Order());
    }

    [Fact]
    public void Enter_picks_the_selected_folder()
    {
        using var picker = RecentPicker();

        picker.Scope.Handle(Key.CursorDown);
        picker.Scope.Handle(Key.Enter);

        Assert.True(picker.Confirmed);
        Assert.Equal("save-conflict", picker.Chosen?.Name);
    }

    [Fact]
    public void Clicking_the_buttons_does_what_their_keys_do()
    {
        using var open = RecentPicker();
        using var cancel = RecentPicker();
        var closed = 0;
        open.Closed += (_, _) => closed++;
        cancel.Closed += (_, _) => closed++;

        open.PickButton.InvokeCommand(Command.Accept);
        cancel.CancelButton.InvokeCommand(Command.Accept);

        Assert.Equal(2, closed);
        Assert.True(open.Confirmed);
        Assert.Equal("sidebar-arrows", open.Chosen?.Name);
        Assert.False(cancel.Confirmed);
    }

    [Theory]
    [InlineData("Draw", "Widget.Draw.cs:3")]
    [InlineData("partial", "Widget.Draw.cs:3,Widget.cs:1")]
    [InlineData("WD", "Widget.Draw.cs:3")]
    public void Definitions_match_the_place_or_the_code(string filter, string expected)
    {
        using var picker = DefinitionPicker();

        picker.FilterField.Text = filter;

        Assert.Equal(expected.Split(',').Order(), picker.VisibleItems.Order());
    }

    [Fact]
    public void Esc_in_Definitions_clears_the_filter_before_it_cancels()
    {
        using var picker = DefinitionPicker();
        var closed = false;
        picker.Closed += (_, _) => closed = true;
        picker.FilterField.Text = "Draw";

        picker.Scope.Handle(Key.Esc);
        var clearedOnly = picker.FilterField.Text.Length == 0 && !closed;
        picker.Scope.Handle(Key.Esc);

        Assert.True(clearedOnly);
        Assert.True(closed);
        Assert.False(picker.Confirmed);
    }

    [Fact]
    public void The_grammar_picker_starts_on_the_current_grammar_and_matches_ids()
    {
        var csharp = Syntax.LanguageById("csharp");
        using var picker = new GrammarPickerView(Syntax.Languages, "Grammar for a.cs", csharp);

        picker.Scope.Handle(Key.Enter);

        Assert.Equal(csharp?.Id, picker.Grammar?.Id);
        using var filtered = new GrammarPickerView(Syntax.Languages, "Grammar for a.cs", null);
        filtered.FilterField.Text = "csharp";
        Assert.Contains(filtered.Shown, r => r.Grammar?.Id == "csharp");
    }

    [Fact]
    public void Go_to_line_shows_Enter_Go_to_and_Esc_Cancel_below_the_field()
    {
        using var view = new GoToLineView(10, 1);

        view.Layout(new System.Drawing.Size(80, 25));
        var field = view.SubViews.OfType<TextField>().Single();
        var buttons = view.SubViews.OfType<Button>().ToList();

        Assert.Equal(["Enter Go to", "Esc Cancel"], buttons.Select(b => b.Text.Trim()));
        Assert.All(buttons, b => Assert.True(b.Frame.Y > field.Frame.Y));
    }

    [Theory]
    [InlineData("7", true, 6, 0)]
    [InlineData("7:3", true, 6, 2)]
    [InlineData("11", false, 0, 0)]
    [InlineData("x", false, 0, 0)]
    public void Go_to_line_accepts_a_line_in_range_and_keeps_the_prompt_open_otherwise(string typed, bool accepted, int row, int column)
    {
        using var view = new GoToLineView(10, 1);
        view.SubViews.OfType<TextField>().Single().Text = typed;

        view.Scope.Handle(Key.Enter);

        Assert.Equal(accepted, view.Confirmed);
        if (accepted) Assert.Equal((row, column), view.Target);
        else Assert.NotEqual(0, view.Alert.Lines);
    }

    [Fact]
    public void The_language_server_dialog_saves_on_Ctrl_Enter_and_shows_both_keys_on_buttons()
    {
        using var dialog = LanguageServer();

        var buttons = dialog.SubViews.OfType<Button>().Select(b => b.Text.Trim()).ToList();
        dialog.Scope.Handle(Key.Enter.WithCtrl);

        Assert.Equal(["Ctrl+Enter Save", "Esc Cancel"], buttons);
        Assert.DoesNotContain(dialog.SubViews, v => v is Label && v.Text.Contains("Esc"));
        Assert.True(dialog.Confirmed);
    }

    [Fact]
    public void Esc_cancels_the_language_server_dialog()
    {
        using var dialog = LanguageServer();

        dialog.Scope.Handle(Key.Esc);

        Assert.False(dialog.Confirmed);
    }

    public static TheoryData<string, string, string[]> Sheets => new()
    {
        { "recent", "Open recent folder", ["Open", "Previous", "Next", "Page up", "Page down", "Cancel"] },
        { "definitions", "Definitions of Widget", ["Go to", "Previous", "Next", "Page up", "Page down", "Cancel"] },
        { "grammar", "Grammar for a.cs", ["Choose", "Previous", "Next", "Page up", "Page down", "Cancel"] },
        { "line", "Go to Line:Column", ["Go to", "Cancel"] },
        { "server", "C# language server", ["Save", "Cancel"] },
    };

    [Theory]
    [MemberData(nameof(Sheets))]
    public void F1_asks_for_a_sheet_titled_with_the_dialog_listing_its_keys(string kind, string title, string[] labels)
    {
        using var dialog = Dialog(kind);
        KeySheet? sheet = null;
        dialog.KeysRequested += (_, s) => sheet = s;

        dialog.Scope.Handle(Key.F1);

        Assert.NotNull(sheet);
        Assert.Null(sheet.Everywhere);
        Assert.Equal(title, sheet.Here!.Heading);
        Assert.Equal(labels.Order(), sheet.Here.Groups.SelectMany(g => g.Rows).Select(r => r.Label).Order());
    }

    private static AppDialog Dialog(string kind) => kind switch
    {
        "line" => new GoToLineView(10, 1),
        "server" => LanguageServer(),
        _ => Picker(kind).Dialog,
    };

    private static RecentFolderPickerView RecentPicker() => new(
    [
        RecentFolderList.Row("/Users/me/code/TuiCode/sidebar-arrows", Home),
        RecentFolderList.Row("/Users/me/code/TuiCode/save-conflict", Home),
        RecentFolderList.Row("/Users/me/code/keyboards/vault", Home),
    ]);

    private static DefinitionPickerView DefinitionPicker() => new("Widget",
    [
        new DefinitionRow(new SourceLocation("/work/Widget.Draw.cs", 2, 0), "Widget.Draw.cs:3", "partial class Widget"),
        new DefinitionRow(new SourceLocation("/work/Widget.cs", 0, 0), "Widget.cs:1", "partial class Widget"),
    ]);

    private static LanguageServerDialog LanguageServer() =>
        new(Syntax.LanguageById("csharp")!, KnownLanguageServers.Default("csharp"), _ => true);

    private static AnyPicker Picker(string kind) => kind switch
    {
        "recent" => AnyPicker.Of(RecentPicker()),
        "definitions" => AnyPicker.Of(DefinitionPicker()),
        _ => AnyPicker.Of(new GrammarPickerView(Syntax.Languages, "Grammar for a.cs", null)),
    };

    private sealed record AnyPicker(AppDialog Dialog, TextField FilterField, Button PickButton, Func<bool> NoMatches) : IDisposable
    {
        public static AnyPicker Of<T>(PickerDialog<T> picker) where T : class =>
            new(picker, picker.FilterField, picker.PickButton, () => picker.NoMatches);

        public void Dispose() => Dialog.Dispose();
    }
}
