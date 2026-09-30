using System.Drawing;
using Terminal.Gui.Views;
using TuiCode.Workbench.About;
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
    public void ShowTerminalCursorColour_marks_a_near_match_and_still_shows_what_the_terminal_reported()
    {
        using var view = new DiagnosticsView("ansi", "No", "#1F2328");

        view.ShowTerminalCursorColour("#202328");

        Assert.Equal("asked #1F2328  •  terminal reports #202328  ✓", view.CursorColourText);
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
    [InlineData(16, 35, "Iterm2Report", 2f, "16 × 35 px  •  iTerm2 report, scale 2.0")]
    [InlineData(8, 17.5f, "Iterm2Report", 1f, "8 × 17.5 px  •  iTerm2 report, scale 1.0")]
    [InlineData(12, 26, "Iterm2Report", 1.5f, "12 × 26 px  •  iTerm2 report, scale 1.5")]
    [InlineData(10, 20, "CellResolutionReply", 1f, "10 × 20 px  •  CSI 16 t")]
    [InlineData(10, 20, "Assumed", 1f, "10 × 20 px  •  no answer, assumed")]
    public void ShowCellSize_says_how_big_a_cell_is_and_where_that_came_from(
        float width, float height, string source, float scale, string expected)
    {
        using var view = new DiagnosticsView("ansi", "No");

        view.ShowCellSize(new CellMeasurement(new SizeF(width, height), Enum.Parse<CellSizeSource>(source), scale));

        Assert.Equal(expected, view.CellSizeText);
    }

    [Theory]
    [InlineData("No")]
    [InlineData("Yes (DisambiguateEscapeCodes, ReportEventTypes, ReportAlternateKeys, ReportAllKeysAsEscapeCodes)")]
    public void Cell_size_row_sits_on_one_line_under_the_cursor_colour_row(string kittyStatus)
    {
        using var view = new DiagnosticsView("ansi", kittyStatus, "#1F2328");
        view.ShowTerminalCursorColour("#FFFFFF");
        view.ShowCellSize(new CellMeasurement(new SizeF(16, 35), CellSizeSource.Iterm2Report, 2));
        view.Layout(new Size(200, 100));

        var labels = view.SubViews.OfType<Label>().ToList();
        var colour = labels.Single(label => label.Text.StartsWith("asked", StringComparison.Ordinal));
        var heading = labels.Single(label => label.Text == "Cell size");
        var cell = labels.Single(label => label.Text == view.CellSizeText);
        var lastKey = labels.Single(label => label.Text == "Last key");
        var footer = labels.Single(label => label.Text == "Esc  close");

        Assert.Equal(colour.Frame.Bottom, cell.Frame.Y);
        Assert.Equal(heading.Frame.Y, cell.Frame.Y);
        Assert.Equal(1, cell.Frame.Height);
        Assert.True(cell.Frame.Width >= cell.Text.Length);
        Assert.True(cell.Frame.Bottom < lastKey.Frame.Y);
        Assert.True(footer.Frame.Bottom <= view.Viewport.Height);
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
