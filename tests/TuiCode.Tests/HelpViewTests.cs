using Terminal.Gui.Views;
using TuiCode.Workbench.Help;

namespace TuiCode.Tests;

public class HelpViewTests : StaticConfigurationTest
{
    private static readonly HelpColumn Diff = new("Diff", [new("Alt+↓", "Next change"), new("Shift+← →", "Page sideways")]);

    [Fact]
    public void A_place_column_sits_beside_everywhere_when_both_fit()
    {
        using var help = new HelpView(Diff, availableWidth: 120);

        Assert.False(help.IsStacked);
        Assert.Equal(["Everywhere", "Diff"], ColumnTitles(help));
    }

    [Fact]
    public void A_place_column_sits_above_everywhere_in_a_narrow_terminal()
    {
        using var help = new HelpView(Diff, availableWidth: 60);

        Assert.True(help.IsStacked);
        Assert.Equal(["Diff", "Everywhere"], ColumnTitles(help));
        help.Layout(new System.Drawing.Size(60, 40));
        Assert.True(help.Frame.Width <= 60);
    }

    [Fact]
    public void Without_a_place_it_shows_everywhere_alone()
    {
        using var help = new HelpView();

        Assert.Equal("Help", help.Title);
        Assert.Equal(["Everywhere"], ColumnTitles(help));
    }

    private static IEnumerable<string> ColumnTitles(HelpView help)
    {
        help.Layout(new System.Drawing.Size(200, 40));
        return help.SubViews
            .Where(view => view.SubViews.OfType<Label>().Any())
            .OrderBy(view => view.Frame.Y).ThenBy(view => view.Frame.X)
            .Select(view => view.SubViews.OfType<Label>().First().Text);
    }
}
