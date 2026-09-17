using TuiCode.Workbench.Parts;

namespace TuiCode.Tests;

public class StatusBarPartTests
{
    [Fact]
    public void Hint_overlays_the_message_and_clearing_it_reverts_to_the_message()
    {
        using var bar = new StatusBarPart();
        bar.SetMessage("/work/a.txt");

        bar.SetHint("Enter next match");
        Assert.Equal("Enter next match", bar.DisplayedText);

        bar.SetHint(null);
        Assert.Equal("/work/a.txt", bar.DisplayedText);
    }

    [Fact]
    public void A_message_set_while_a_hint_shows_appears_once_the_hint_clears()
    {
        using var bar = new StatusBarPart();
        bar.SetHint("Enter next match");

        bar.SetMessage("Saved: /work/a.txt");
        Assert.Equal("Enter next match", bar.DisplayedText);

        bar.SetHint(null);
        Assert.Equal("Saved: /work/a.txt", bar.DisplayedText);
    }

    [Fact]
    public void The_grammar_follows_the_message_after_a_separator()
    {
        using var bar = new StatusBarPart();
        bar.SetMessage("/work/a.cs");

        bar.SetGrammar("C#");
        Assert.Equal("/work/a.cs  •  C#", bar.DisplayedText);

        bar.SetGrammar(null);
        Assert.Equal("/work/a.cs", bar.DisplayedText);
    }

    [Fact]
    public void An_in_flight_chord_hides_the_grammar()
    {
        using var bar = new StatusBarPart();
        bar.SetGrammar("C#");

        bar.SetChord("Ctrl+G");

        Assert.Equal("Ctrl+G…", bar.DisplayedText);
    }

    [Fact]
    public void An_in_flight_chord_takes_precedence_over_a_hint()
    {
        using var bar = new StatusBarPart();
        bar.SetHint("Enter next match");

        bar.SetChord("Ctrl+G");

        Assert.Equal("Ctrl+G…", bar.DisplayedText);
    }
}
