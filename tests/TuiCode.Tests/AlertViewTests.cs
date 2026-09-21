using Terminal.Gui.ViewBase;
using TuiCode.Workbench.Controls;

namespace TuiCode.Tests;

public class AlertViewTests : StaticConfigurationTest
{
    private const int Width = 20;

    [Fact]
    public void A_message_that_fits_takes_a_single_row()
    {
        using var alert = new AlertView(Width);

        alert.Show("Refused", AlertSeverity.Error);

        Assert.Equal(1, alert.Lines);
        Assert.True(alert.Visible);
    }

    [Fact]
    public void A_message_wider_than_the_block_wraps_onto_further_rows()
    {
        using var alert = new AlertView(Width);

        alert.Show("GraphQL: Can not approve your own pull request", AlertSeverity.Error);

        Assert.True(alert.Lines > 1);
        Assert.All(alert.Text.Split('\n'), line => Assert.True(line.Length <= Width, $"'{line}' overflows"));
    }

    [Fact]
    public void The_message_is_kept_as_it_was_given_not_as_it_wrapped()
    {
        using var alert = new AlertView(Width);

        alert.Show("GraphQL: Can not approve your own pull request", AlertSeverity.Error);

        Assert.Equal("GraphQL: Can not approve your own pull request", alert.Message);
    }

    [Fact]
    public void With_nothing_to_say_it_takes_no_room_at_all()
    {
        using var alert = new AlertView(Width);
        alert.Show("Submitting…", AlertSeverity.Info);

        alert.Clear();

        Assert.Equal(0, alert.Lines);
        Assert.False(alert.Visible);
        Assert.Equal("", alert.Message);
    }
}
