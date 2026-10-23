using MentalDesk.Tui;
using Terminal.Gui.ViewBase;
using TuiCode.Workbench.Parts;

namespace TuiCode.Tests;

public class StatusBarPartTests
{
    private readonly FakeClock _clock = new();

    private StatusBarPart Bar(int width = 80)
    {
        var bar = new StatusBarPart { Width = width };
        bar.Bar.Clock = _clock;
        return bar;
    }

    [Fact]
    public void SetHelpKey_names_the_key_at_the_end_and_leaves_the_message_alone()
    {
        using var bar = Bar();
        bar.SetResting("/work/a.txt");

        bar.SetHelpKey("F3");

        Assert.Equal("Press F3 for help", bar.DisplayedHelp);
        Assert.Equal("/work/a.txt", bar.DisplayedText);
    }

    [Fact]
    public void With_nothing_to_say_the_bar_shows_the_resting_text_or_TuiCode()
    {
        using var bar = Bar();

        var idle = bar.DisplayedText;
        bar.SetResting("/work/a.txt");
        var file = bar.DisplayedText;
        bar.SetResting(null);

        Assert.Equal(("TuiCode", "/work/a.txt", "TuiCode"), (idle, file, bar.DisplayedText));
    }

    [Fact]
    public void A_reply_goes_at_the_next_key_and_the_path_comes_back()
    {
        using var bar = Bar();
        bar.SetResting("/work/a.txt");
        bar.ShowMessage("Saved: /work/a.txt");
        var said = bar.DisplayedText;

        bar.Bar.KeyPressed();

        Assert.Equal("Saved: /work/a.txt", said);
        Assert.Equal("/work/a.txt", bar.DisplayedText);
    }

    [Fact]
    public void A_reply_goes_after_five_seconds_with_no_key()
    {
        using var bar = Bar();
        bar.SetResting("/work/a.txt");
        bar.ShowMessage("Copied 3 lines");

        _clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal("/work/a.txt", bar.DisplayedText);
    }

    [Fact]
    public void An_error_that_happened_by_itself_outlasts_typing_and_goes_at_the_first_key_after_five_seconds()
    {
        using var bar = Bar();
        bar.ShowNews("Couldn't refresh the review: gh: HTTP 502", Severity.Error);

        bar.Bar.KeyPressed();
        _clock.Advance(TimeSpan.FromSeconds(6));
        var later = (bar.DisplayedText, bar.ShowsError);
        bar.Bar.KeyPressed();

        Assert.Equal(("Couldn't refresh the review: gh: HTTP 502", true), later);
        Assert.Equal("TuiCode", bar.DisplayedText);
        Assert.Equal("⚠ 1", bar.DisplayedWarnings);
    }

    [Fact]
    public void Withdraw_takes_a_message_off_only_while_it_shows()
    {
        using var bar = Bar();
        var cut = bar.ShowMessage("Cut: /work/a.txt");
        bar.ShowMessage("Cut: /work/b.txt");

        bar.Withdraw(cut);
        var other = bar.DisplayedText;
        bar.Withdraw(bar.Bar.Current);

        Assert.Equal("Cut: /work/b.txt", other);
        Assert.Equal(bar.DefaultMessage, bar.DisplayedText);
    }

    [Fact]
    public void The_grammar_mode_wrap_diff_and_position_sit_together_at_the_right_not_after_the_message()
    {
        using var bar = Bar();
        bar.SetResting("/work/a.cs");

        bar.SetGrammar("C#");
        bar.SetMode("Column select");
        bar.SetWrap(true);
        bar.SetDiffStatus("File 2 of 7  •  Change 1 of 4");
        bar.SetPosition((41, 8));

        Assert.Equal("/work/a.cs", bar.DisplayedText);
        Assert.Equal("C#  •  Column select  •  Wrap  •  File 2 of 7  •  Change 1 of 4  •  Ln 42, Col 9", bar.DisplayedState);
    }

    [Fact]
    public void An_in_flight_chord_shows_in_the_message_slot()
    {
        using var bar = Bar();
        bar.SetGrammar("C#");

        bar.SetChord("Ctrl+G");

        Assert.Equal("Ctrl+G…", bar.DisplayedText);
        Assert.Equal("C#", bar.DisplayedState);
    }

    [Fact]
    public void Position_shows_one_based_and_null_hides_it()
    {
        using var bar = Bar();

        bar.SetPosition((41, 6));
        Assert.Equal("Ln 42, Col 7", bar.DisplayedPosition);

        bar.SetPosition(null);
        Assert.Equal("", bar.DisplayedPosition);
    }

    [Fact]
    public void The_help_hint_ends_the_bar_with_the_position_before_it()
    {
        using var bar = Bar(60);
        bar.SetPosition((0, 0));

        bar.Layout(new System.Drawing.Size(60, 1));

        var help = Find(bar, "Press F1 for help");
        var position = Find(bar, "Ln 1, Col 1");
        Assert.Equal(60 - 1, help.FrameToScreen().Right);
        Assert.Equal(help.FrameToScreen().X - 5, position.FrameToScreen().Right);
    }

    [Fact]
    public void An_unbound_help_key_leaves_the_position_at_the_right_edge()
    {
        using var bar = Bar(40);
        bar.SetPosition((0, 0));

        bar.SetHelpKey(null);
        bar.Layout(new System.Drawing.Size(40, 1));

        Assert.Equal("", bar.DisplayedHelp);
        Assert.Equal(40 - 1, Find(bar, "Ln 1, Col 1").FrameToScreen().Right);
    }

    [Fact]
    public void A_selection_at_one_caret_shows_its_count_after_the_position()
    {
        using var bar = Bar();
        bar.SetPosition((11, 4));

        bar.SetSelection(1, 34);

        Assert.Equal("Ln 12, Col 5 (34 selected)", bar.DisplayedPosition);
    }

    [Fact]
    public void Several_carets_show_how_many_in_place_of_the_position()
    {
        using var bar = Bar();
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
        using var bar = Bar();
        bar.SetPosition((11, 4));
        bar.SetSelection(3, 96);

        bar.SetSelection(1, null);

        Assert.Equal("Ln 12, Col 5", bar.DisplayedPosition);
    }

    [Fact]
    public void A_long_message_stops_short_of_the_right_end()
    {
        using var bar = Bar(60);
        bar.ShowMessage(new string('x', 80));
        bar.SetGrammar("C#");
        bar.SetPosition((0, 0));

        bar.Layout(new System.Drawing.Size(60, 1));

        var message = Find(bar, new string('x', 80));
        var grammar = Find(bar, "C#");
        Assert.True(message.FrameToScreen().Right < grammar.FrameToScreen().X);
        Assert.Equal(60 - 1, Find(bar, "Press F1 for help").FrameToScreen().Right);
    }

    private static View Find(View root, string text) => TryFind(root, text)!;

    private static View? TryFind(View root, string text) =>
        root.Text == text ? root : root.SubViews.Select(view => TryFind(view, text)).FirstOrDefault(view => view is not null);
}
