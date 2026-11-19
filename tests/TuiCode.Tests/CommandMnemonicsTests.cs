using TuiCode.Abstractions;
using TuiCode.Workbench.Mnemonics;

namespace TuiCode.Tests;

public class CommandMnemonicsTests
{
    [Fact]
    public void Mnemonics_are_unique()
    {
        var mnemonics = CommandMnemonics.All.Select(kv => kv.Value).ToList();
        Assert.Equal(mnemonics.Count, mnemonics.Distinct().Count());
    }

    [Fact]
    public void Every_mnemonic_is_reachable_and_only_q_needs_Enter()
    {
        var entries = CommandMnemonics.All.Select(kv => new MnemonicEntry(kv.Key, kv.Value, kv.Key)).ToList();
        var needsEnter = new List<string>();
        foreach (var entry in entries)
        {
            var ran = new List<string>();
            using var view = new MnemonicView(entries, ran.Add);
            foreach (var c in entry.Mnemonic) view.Scope.Handle(new Key(c));
            if (ran.Count == 0)
            {
                needsEnter.Add(entry.Mnemonic);
                view.Scope.Handle(Key.Enter);
            }
            Assert.Equal([entry.CommandId], ran);
        }
        Assert.Equal(["q"], needsEnter);
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
        Assert.Equal("fs", CommandMnemonics.For(CommandIds.FocusSidebar));
        Assert.Equal("wp", CommandMnemonics.For(CommandIds.WidenPane));
        Assert.Equal("np", CommandMnemonics.For(CommandIds.NarrowPane));
        Assert.Equal("nt", CommandMnemonics.For(CommandIds.NextEditor));
        Assert.Equal("pt", CommandMnemonics.For(CommandIds.PreviousEditor));
        Assert.Equal("gl", CommandMnemonics.For(CommandIds.GoToLine));
        Assert.Equal("gp", CommandMnemonics.For(CommandIds.NavigateBack));
        Assert.Equal("gn", CommandMnemonics.For(CommandIds.NavigateForward));
        Assert.Equal("ff", CommandMnemonics.For(CommandIds.FindInFile));
        Assert.Equal("rf", CommandMnemonics.For(CommandIds.ReplaceInFile));
        Assert.Equal("se", CommandMnemonics.For(CommandIds.ShowExplorer));
        Assert.Equal("fg", CommandMnemonics.For(CommandIds.FindGlobally));
        Assert.Equal("rg", CommandMnemonics.For(CommandIds.ReplaceGlobally));
        Assert.Equal("tui", CommandMnemonics.For(CommandIds.ShowAbout));
        Assert.Equal("sts", CommandMnemonics.For(CommandIds.SuspendToShell));
        Assert.Equal("gi", CommandMnemonics.For(CommandIds.GoToImplementation));
        Assert.Equal("qi", CommandMnemonics.For(CommandIds.QuickInfo));
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
