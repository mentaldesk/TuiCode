using Terminal.Gui.Views;
using TuiCode.Workbench.Mnemonics;

namespace TuiCode.Tests;

// The leader lists only the commands it was handed, and a key no listed mnemonic could complete is
// ignored rather than aborting the sequence (#285). Not hosted in a running app: keys go through the
// capture scope the host would push.
public class MnemonicViewTests
{
    private static readonly MnemonicEntry[] Explorer =
    [
        new("cmd.quit", "q", "Quit"),
        new("cmd.focusEditor", "fe", "Focus editor"),
        new("cmd.renameFile", "mf", "Move or rename file or folder"),
    ];

    [Fact]
    public void The_list_is_what_the_host_handed_it()
    {
        using var view = Build(out _);

        Assert.Equal(["fe", "mf", "q"], view.Mnemonics);
    }

    [Fact]
    public void A_key_no_listed_mnemonic_could_complete_leaves_the_prefix_alone()
    {
        using var view = Build(out var ran);

        Type(view, 'm');
        Assert.Equal(["mf"], view.Mnemonics);
        Assert.Equal("Mnemonic: m", Header(view));

        // 'u' would have completed mu — out of scope, so nothing happens at all.
        Type(view, 'u');

        Assert.Equal(["mf"], view.Mnemonics);
        Assert.Equal("Mnemonic: m", Header(view));
        Assert.Empty(ran);
    }

    [Fact]
    public void A_key_that_completes_a_listed_mnemonic_still_fires_it()
    {
        using var view = Build(out var ran);

        Type(view, 'm');
        Type(view, 'f');

        Assert.Equal(["cmd.renameFile"], ran);
    }

    // Enter is an explicit key, not auto-fire, so the lone remaining match commits on it.
    [Fact]
    public void Enter_commits_the_lone_remaining_match()
    {
        using var view = Build(out var ran);

        Type(view, 'm');
        view.Scope.Handle(Key.Enter);

        Assert.Equal(["cmd.renameFile"], ran);
    }

    [Fact]
    public void Backspace_re_widens_the_list()
    {
        using var view = Build(out _);

        Type(view, 'm');
        view.Scope.Handle(Key.Backspace);

        Assert.Equal(["fe", "mf", "q"], view.Mnemonics);
    }

    private static MnemonicView Build(out List<string> ran)
    {
        var executed = new List<string>();
        ran = executed;
        return new MnemonicView(Explorer, executed.Add);
    }

    private static void Type(MnemonicView view, char c) => view.Scope.Handle(new Key(c));

    private static string Header(MnemonicView view) =>
        view.SubViews.OfType<Label>().Single().Text;
}
