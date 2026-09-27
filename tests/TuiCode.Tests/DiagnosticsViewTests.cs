using Terminal.Gui.Views;
using TuiCode.Workbench.Diagnostics;

namespace TuiCode.Tests;

public class DiagnosticsViewTests
{
    [Fact]
    public void Ctor_wraps_long_kitty_status_across_multiple_lines()
    {
        const string status = "Yes (DisambiguateEscapeCodes, ReportEventTypes, ReportAlternateKeys, ReportAllKeysAsEscapeCodes)";

        using var view = new DiagnosticsView("ansi", status);

        var kittyLabel = view.SubViews
            .OfType<Label>()
            .Single(label => label.Text.Contains("DisambiguateEscapeCodes", StringComparison.Ordinal));

        Assert.Contains("\n", kittyLabel.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Ctor_breaks_long_unspaced_kitty_status_values()
    {
        var status = "Yes (" + new string('X', 80) + ")";

        using var view = new DiagnosticsView("ansi", status);

        var kittyLabel = view.SubViews
            .OfType<Label>()
            .Single(label => label.Text.Contains("Yes (", StringComparison.Ordinal));

        Assert.Contains("\n", kittyLabel.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTerminalCursorColour_marks_a_match()
    {
        using var view = new DiagnosticsView("ansi", "No", "#1F2328");

        view.ShowTerminalCursorColour("#1F2328");

        Assert.Equal("asked #1F2328  •  terminal reports #1F2328  ✓", view.CursorColourText);
    }

    [Fact]
    public void ShowTerminalCursorColour_marks_a_mismatch()
    {
        using var view = new DiagnosticsView("ansi", "No", "#1F2328");

        view.ShowTerminalCursorColour("#FFFFFF");

        Assert.Equal("asked #1F2328  •  terminal reports #FFFFFF  ✗", view.CursorColourText);
    }

    [Fact]
    public void ShowTerminalCursorColour_says_when_the_terminal_did_not_answer()
    {
        using var view = new DiagnosticsView("ansi", "No", "#1F2328");

        view.ShowTerminalCursorColour(null);

        Assert.Equal("asked #1F2328  •  terminal didn't answer", view.CursorColourText);
    }

    [Theory]
    [InlineData("No")]
    [InlineData("Yes (DisambiguateEscapeCodes, ReportEventTypes, ReportAlternateKeys, ReportAllKeysAsEscapeCodes)")]
    public void Cursor_colour_row_fits_between_the_kitty_row_and_the_footer(string kittyStatus)
    {
        using var view = new DiagnosticsView("ansi", kittyStatus, "#1F2328");
        view.ShowTerminalCursorColour("#FFFFFF");
        view.Layout(new System.Drawing.Size(200, 100));

        var labels = view.SubViews.OfType<Label>().ToList();
        var kitty = labels.Single(label => label.Text.StartsWith(kittyStatus[..2], StringComparison.Ordinal));
        var colour = labels.Single(label => label.Text.StartsWith("asked", StringComparison.Ordinal));
        var lastKey = labels.Single(label => label.Text == "Last key");
        var footer = labels.Single(label => label.Text == "Esc  close");

        Assert.Equal(kitty.Frame.Bottom, colour.Frame.Y);
        Assert.True(colour.Frame.Bottom < lastKey.Frame.Y);
        Assert.True(footer.Frame.Bottom <= view.Viewport.Height);
        Assert.Equal(view.CursorColourText, colour.Text);
        Assert.True(colour.Frame.Width >= colour.Text.Length);
    }
}
