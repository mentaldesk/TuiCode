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

    [Fact]
    public void A_column_taller_than_the_room_scrolls_to_its_last_row()
    {
        var rows = Enumerable.Range(1, 30).Select(i => new HelpRow($"F{i}", $"Command {i}")).ToList();
        using var help = new HelpView(new HelpColumn("Editor", rows), availableWidth: 80, availableHeight: 22);
        help.Layout(new System.Drawing.Size(80, 22));

        Assert.True(help.IsScrollable);
        Assert.Equal(22, help.Frame.Height);
        Assert.Equal(0, help.Body.Viewport.Y);
        Assert.True(help.Body.Viewport.Width >= help.Body.GetContentSize().Width);

        for (var i = 0; i < 10; i++) help.Scope.Handle(Key.PageDown);

        Assert.Equal(help.Body.GetContentSize().Height, help.Body.Viewport.Bottom);
        help.Scope.Handle(Key.CursorUp);
        Assert.Equal(help.Body.GetContentSize().Height - 1, help.Body.Viewport.Bottom);
    }

    [Fact]
    public void A_column_that_fits_does_not_scroll()
    {
        using var help = new HelpView(Diff, availableWidth: 120, availableHeight: 40);

        Assert.False(help.IsScrollable);
    }

    private static IEnumerable<string> ColumnTitles(HelpView help)
    {
        help.Layout(new System.Drawing.Size(200, 40));
        return help.Body.SubViews
            .Where(view => view.SubViews.OfType<Label>().Any())
            .OrderBy(view => view.Frame.Y).ThenBy(view => view.Frame.X)
            .Select(view => view.SubViews.OfType<Label>().First().Text);
    }
}
