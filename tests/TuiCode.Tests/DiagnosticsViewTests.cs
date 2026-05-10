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
}

