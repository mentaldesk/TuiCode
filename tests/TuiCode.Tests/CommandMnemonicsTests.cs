using TuiCode.Abstractions;

namespace TuiCode.Tests;

public class CommandMnemonicsTests
{
    [Fact]
    public void Mnemonics_are_unique()
    {
        var mnemonics = CommandMnemonics.All.Select(kv => kv.Value).ToList();
        Assert.Equal(mnemonics.Count, mnemonics.Distinct().Count());
    }

    // The auto-execute rule (MnemonicResolver.ResolveExact) only works if no complete
    // mnemonic is a prefix of another — otherwise the shorter one could never fire without Enter.
    [Fact]
    public void No_mnemonic_is_a_prefix_of_another()
    {
        var mnemonics = CommandMnemonics.All.Select(kv => kv.Value).ToList();
        foreach (var a in mnemonics)
            foreach (var b in mnemonics)
            {
                if (ReferenceEquals(a, b) || a == b) continue;
                Assert.False(b.StartsWith(a, StringComparison.Ordinal),
                    $"Mnemonic '{a}' is a prefix of '{b}' — one of them would be unreachable.");
            }
    }

    [Fact]
    public void For_returns_null_for_commands_without_a_mnemonic()
    {
        Assert.Null(CommandMnemonics.For(CommandIds.ShowActions));
        Assert.Null(CommandMnemonics.For(CommandIds.ShowMnemonics));
    }

    [Fact]
    public void For_returns_the_agreed_mnemonics()
    {
        Assert.Equal("q", CommandMnemonics.For(CommandIds.Quit));
        Assert.Equal("?", CommandMnemonics.For(CommandIds.ShowHelp));
        Assert.Equal("sf", CommandMnemonics.For(CommandIds.SaveActiveEditor));
        Assert.Equal("cf", CommandMnemonics.For(CommandIds.CloseActiveEditor));
        Assert.Equal("ts", CommandMnemonics.For(CommandIds.ToggleSidebar));
        Assert.Equal("tn", CommandMnemonics.For(CommandIds.NextEditor));
        Assert.Equal("tp", CommandMnemonics.For(CommandIds.PreviousEditor));
        Assert.Equal("gl", CommandMnemonics.For(CommandIds.GoToLine));
    }

    [Fact]
    public void Focus_editor_tabs_get_f1_through_f9()
    {
        for (var i = 1; i <= 9; i++)
            Assert.Equal($"f{i}", CommandMnemonics.For(CommandIds.FocusEditorByIndex(i)));
    }

    // The leader binding (WorkbenchHost.BindDefaults) throws at startup if this can't parse.
    [Fact]
    public void Leader_key_sequence_parses()
    {
        Assert.True(Key.TryParse("Ctrl+Space", out _));
    }
}
