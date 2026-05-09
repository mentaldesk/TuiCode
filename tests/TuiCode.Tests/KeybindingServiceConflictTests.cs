using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class KeybindingServiceConflictTests
{
    [Fact]
    public void Unbind_returns_true_and_removes_a_known_binding()
    {
        var (service, _) = Build();
        service.Bind("Ctrl+S", "save");

        Assert.True(service.Unbind("Ctrl+S"));
        Assert.False(service.Bindings.Any());
    }

    [Fact]
    public void Unbind_returns_false_when_the_sequence_is_not_bound()
    {
        var (service, _) = Build();

        Assert.False(service.Unbind("Ctrl+S"));
    }

    [Fact]
    public void Bindings_enumerates_single_and_chord_sequences()
    {
        var (service, _) = Build();
        service.Bind("Ctrl+S", "save");
        service.Bind("Ctrl+W X", "close.editor");

        var bindings = service.Bindings.OrderBy(b => b.CommandId).ToList();

        // Bare-letter chord steps are normalized to lower-case in the trie, so the enumerator
        // emits "Ctrl+W x" not "Ctrl+W X". UI rendering can casefold for display.
        Assert.Collection(bindings,
            b => { Assert.Equal("Ctrl+W x", b.Sequence); Assert.Equal("close.editor", b.CommandId); },
            b => { Assert.Equal("Ctrl+S", b.Sequence); Assert.Equal("save", b.CommandId); });
    }

    [Fact]
    public void Reset_clears_all_bindings()
    {
        var (service, _) = Build();
        service.Bind("Ctrl+S", "save");

        service.Reset();

        Assert.Empty(service.Bindings);
    }

    [Fact]
    public void CheckConflict_returns_null_for_an_unrelated_sequence()
    {
        var (service, _) = Build();
        service.Bind("Ctrl+S", "save");

        Assert.Null(service.CheckConflict("Ctrl+K"));
    }

    [Fact]
    public void CheckConflict_returns_ExactMatch_for_a_bound_sequence()
    {
        var (service, _) = Build();
        service.Bind("Ctrl+S", "save");

        Assert.Equal(KeybindingConflict.ExactMatch, service.CheckConflict("Ctrl+S"));
    }

    [Fact]
    public void CheckConflict_returns_PrefixOfExisting_when_proposed_would_shadow_a_chord()
    {
        var (service, _) = Build();
        service.Bind("Ctrl+W X", "close.editor");

        Assert.Equal(KeybindingConflict.PrefixOfExisting, service.CheckConflict("Ctrl+W"));
    }

    [Fact]
    public void CheckConflict_returns_ExtensionOfExisting_when_a_shorter_binding_already_fires()
    {
        var (service, _) = Build();
        service.Bind("Ctrl+W", "close.editor");

        Assert.Equal(KeybindingConflict.ExtensionOfExisting, service.CheckConflict("Ctrl+W X"));
    }

    private static (KeybindingService service, CommandService commands) Build()
    {
        var commands = new CommandService();
        var service = new KeybindingService(commands);
        return (service, commands);
    }
}
