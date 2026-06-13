using TuiCode.Workbench.Mnemonics;

namespace TuiCode.Tests;

// Pure matching logic — no Terminal.Gui, so these stay parallel and need no base class.
public class MnemonicResolverTests
{
    private static MnemonicResolver Build() => new(new[]
    {
        new MnemonicEntry("cmd.closeFile", "cf", "Close active editor"),
        new MnemonicEntry("cmd.focusEditor", "fe", "Focus editor"),
        new MnemonicEntry("cmd.focusTabStrip", "ft", "Focus editor tab strip"),
        new MnemonicEntry("cmd.focus1", "f1", "Focus editor tab 1"),
        new MnemonicEntry("cmd.quit", "q", "Quit"),
        new MnemonicEntry("cmd.toggleSidebar", "ts", "Toggle sidebar"),
    });

    [Fact]
    public void All_is_ordered_by_mnemonic()
    {
        var resolver = Build();
        Assert.Equal(new[] { "cf", "f1", "fe", "ft", "q", "ts" }, resolver.All.Select(e => e.Mnemonic));
    }

    [Fact]
    public void Matching_empty_prefix_returns_everything()
    {
        var resolver = Build();
        Assert.Equal(resolver.All.Count, resolver.Matching("").Count);
    }

    [Fact]
    public void Matching_filters_to_the_prefix_family()
    {
        var resolver = Build();
        Assert.Equal(new[] { "f1", "fe", "ft" }, resolver.Matching("f").Select(e => e.Mnemonic));
    }

    [Fact]
    public void ResolveExact_returns_null_for_empty_prefix()
    {
        Assert.Null(Build().ResolveExact(""));
    }

    [Fact]
    public void ResolveExact_returns_null_for_unknown_prefix()
    {
        Assert.Null(Build().ResolveExact("z"));
    }

    [Fact]
    public void ResolveExact_returns_null_while_a_prefix_is_still_ambiguous()
    {
        // 'f' could still become fe, ft, or f1 — don't fire yet.
        Assert.Null(Build().ResolveExact("f"));
    }

    [Fact]
    public void ResolveExact_fires_a_single_key_mnemonic()
    {
        Assert.Equal("cmd.quit", Build().ResolveExact("q")?.CommandId);
    }

    [Fact]
    public void ResolveExact_fires_a_completed_family_mnemonic()
    {
        Assert.Equal("cmd.focusEditor", Build().ResolveExact("fe")?.CommandId);
        Assert.Equal("cmd.focus1", Build().ResolveExact("f1")?.CommandId);
    }

    [Fact]
    public void ResolveExact_waits_for_the_second_key_even_when_a_family_has_one_member()
    {
        // 'c' uniquely points at cf today, but the family is reserved (co/ca/cs to come),
        // so a bare 'c' must not auto-fire — the user types the full cf.
        Assert.Null(Build().ResolveExact("c"));
        Assert.Equal("cmd.closeFile", Build().ResolveExact("cf")?.CommandId);
    }
}
