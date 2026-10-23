using TuiCode.Workbench.Parts;

namespace TuiCode.Tests;

public class StatusBarPartTests
{
    [Fact]
    public void SetHelpKey_names_the_key_at_the_end_and_leaves_the_message_alone()
    {
        using var bar = new StatusBarPart();
        bar.SetMessage("/work/a.txt");

        bar.SetHelpKey("F3");

        Assert.Equal("Press F3 for help", bar.DisplayedHelp);
        Assert.Equal("/work/a.txt", bar.DisplayedText);
    }

    [Fact]
    public void ClearMessage_reverts_to_the_default_only_while_that_message_shows()
    {
        using var bar = new StatusBarPart();
        bar.SetMessage("Cut: /work/a.txt");

        bar.ClearMessage("Cut: /work/b.txt");
        Assert.Equal("Cut: /work/a.txt", bar.DisplayedText);

        bar.ClearMessage("Cut: /work/a.txt");
        Assert.Equal(bar.DefaultMessage, bar.DisplayedText);
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
    public void Position_shows_one_based_and_null_hides_it()
    {
        using var bar = new StatusBarPart();

        bar.SetPosition((41, 6));
        Assert.Equal("Ln 42, Col 7", bar.DisplayedPosition);

        bar.SetPosition(null);
        Assert.Equal("", bar.DisplayedPosition);
    }

    [Fact]
    public void The_help_hint_ends_the_bar_with_or_without_a_position()
    {
        using var bar = new StatusBarPart { Width = 60 };
        bar.Layout(new System.Drawing.Size(60, 1));
        var idle = bar.DisplayedHelp;

        bar.SetPosition((0, 0));
        bar.Layout(new System.Drawing.Size(60, 1));

        var help = bar.SubViews.Single(v => v.Text == "Press F1 for help");
        var position = bar.SubViews.Single(v => v.Text == "Ln 1, Col 1");
        Assert.Equal("Press F1 for help", idle);
        Assert.Equal(60 - 1, help.Frame.Right);
        Assert.Equal(help.Frame.X - 3, position.Frame.Right);
    }

    [Fact]
    public void A_message_never_replaces_the_help_hint()
    {
        using var bar = new StatusBarPart();

        bar.SetMessage("Saved a.txt");

        Assert.Equal("Press F1 for help", bar.DisplayedHelp);
    }

    [Fact]
    public void An_unbound_help_key_leaves_the_position_at_the_right_edge()
    {
        using var bar = new StatusBarPart { Width = 40 };
        bar.SetPosition((0, 0));

        bar.SetHelpKey(null);
        bar.Layout(new System.Drawing.Size(40, 1));

        var position = bar.SubViews.Single(v => v.Text == "Ln 1, Col 1");
        Assert.Equal("", bar.DisplayedHelp);
        Assert.Equal(40 - 1, position.Frame.Right);
    }

    [Fact]
    public void A_selection_at_one_caret_shows_its_count_after_the_position()
    {
        using var bar = new StatusBarPart();
        bar.SetPosition((11, 4));

        bar.SetSelection(1, 34);

        Assert.Equal("Ln 12, Col 5 (34 selected)", bar.DisplayedPosition);
    }

    [Fact]
    public void Several_carets_show_how_many_in_place_of_the_position()
    {
        using var bar = new StatusBarPart();
        bar.SetPosition((11, 4));

        bar.SetSelection(3, 96);
        var selecting = bar.DisplayedPosition;
        bar.SetSelection(3, null);

        Assert.Equal("3 selections (96 selected)", selecting);
        Assert.Equal("3 selections", bar.DisplayedPosition);
    }

    [Fact]
    public void Clearing_the_selection_goes_back_to_the_position()
    {
        using var bar = new StatusBarPart();
        bar.SetPosition((11, 4));
        bar.SetSelection(3, 96);

        bar.SetSelection(1, null);

        Assert.Equal("Ln 12, Col 5", bar.DisplayedPosition);
    }

    [Fact]
    public void Position_sits_before_the_help_hint_and_a_long_message_stops_short_of_it()
    {
        using var bar = new StatusBarPart { Width = 40 };
        bar.SetMessage(new string('x', 60));
        bar.SetPosition((0, 0));

        bar.Layout(new System.Drawing.Size(40, 1));

        var position = bar.SubViews.Single(v => v.Text == "Ln 1, Col 1");
        var message = bar.SubViews.Single(v => v.Text == new string('x', 60));
        var help = bar.SubViews.Single(v => v.Text == "Press F1 for help");
        Assert.Equal(40 - 1, help.Frame.Right);
        Assert.Equal(help.Frame.X - 3, position.Frame.Right);
        Assert.Equal(position.Frame.X - 2, message.Frame.Right);
    }
}
