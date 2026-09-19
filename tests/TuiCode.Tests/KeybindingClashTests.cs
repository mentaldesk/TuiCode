using TuiCode.Abstractions;
using TuiCode.Workbench.Settings;
using KeyBinding = TuiCode.Abstractions.KeyBinding;

namespace TuiCode.Tests;

// The Keyboard Shortcuts picker's conflict rules per scope (#142).
public class KeybindingClashTests
{
    [Theory]
    [InlineData("Ctrl+K", "Ctrl+K", KeybindingConflict.ExactMatch)]
    [InlineData("Ctrl+K Ctrl+S", "Ctrl+K", KeybindingConflict.PrefixOfExisting)]
    [InlineData("Ctrl+K", "Ctrl+K Ctrl+S", KeybindingConflict.ExtensionOfExisting)]
    public void Find_reports_a_clash_in_the_same_scope_as_a_conflict(string existing, string candidate, KeybindingConflict kind)
    {
        var clash = KeybindingClash.Find([Binding(existing, "a", CommandScope.Explorer)], Binding(candidate, "b", CommandScope.Explorer));

        Assert.NotNull(clash);
        Assert.Equal(kind, clash.Kind);
        Assert.False(clash.IsWarning);
    }

    [Fact]
    public void Find_ignores_bindings_in_another_non_global_scope()
    {
        var clash = KeybindingClash.Find([Binding("Ctrl+X", "cut", CommandScope.Explorer)], Binding("Ctrl+X", "b", CommandScope.Find));

        Assert.Null(clash);
    }

    [Theory]
    [InlineData(CommandScope.Explorer, CommandScope.Global)]
    [InlineData(CommandScope.Global, CommandScope.Explorer)]
    public void Find_warns_about_a_scoped_binding_against_a_global_one(CommandScope existing, CommandScope candidate)
    {
        var clash = KeybindingClash.Find([Binding("Ctrl+V", "a", existing)], Binding("Ctrl+V", "b", candidate));

        Assert.NotNull(clash);
        Assert.True(clash.IsWarning);
    }

    [Fact]
    public void Find_prefers_a_same_scope_conflict_over_a_warning()
    {
        var clash = KeybindingClash.Find(
            [Binding("Ctrl+V", "open", CommandScope.Global), Binding("Ctrl+V", "paste", CommandScope.Explorer)],
            Binding("Ctrl+V", "cut", CommandScope.Explorer));

        Assert.NotNull(clash);
        Assert.Equal("paste", clash.Existing.CommandId);
        Assert.False(clash.IsWarning);
    }

    [Fact]
    public void Message_names_the_scope_of_the_binding_it_would_replace()
    {
        var clash = KeybindingClash.Find([Binding("Ctrl+V", "paste", CommandScope.Explorer)], Binding("Ctrl+V", "cut", CommandScope.Explorer))!;

        Assert.Equal("Ctrl+V is already \"Paste file or folder\"\nin Explorer. Replace it?", clash.Message(Label));
    }

    [Fact]
    public void Message_for_a_global_binding_says_the_scoped_one_wins_while_it_has_focus()
    {
        var clash = KeybindingClash.Find([Binding("Ctrl+V", "paste", CommandScope.Explorer)], Binding("Ctrl+V", "open", CommandScope.Global))!;

        Assert.Equal(
            "Ctrl+V is \"Paste file or folder\"\nin Explorer, which wins while the explorer\nhas focus. Bind it globally anyway?",
            clash.Message(Label));
    }

    [Fact]
    public void Message_for_a_scoped_binding_says_it_wins_over_the_global_one()
    {
        var clash = KeybindingClash.Find([Binding("Ctrl+O", "open", CommandScope.Global)], Binding("Ctrl+O", "paste", CommandScope.Find))!;

        Assert.Equal(
            "Ctrl+O is \"Open\"\nglobally. The new binding wins while\nthe Find sidebar has focus. Bind it anyway?",
            clash.Message(Label));
    }

    private static string Label(string id) => id switch
    {
        "paste" => "Paste file or folder",
        "open" => "Open",
        _ => id,
    };

    private static KeyBinding Binding(string keys, string command, CommandScope scope) => new(TestKeys.Chord(keys), command, scope);
}
