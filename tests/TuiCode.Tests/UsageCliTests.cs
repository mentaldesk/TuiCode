using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.TerminalIntegration;

namespace TuiCode.Tests;

public class UsageCliTests
{
    [Fact]
    public void TryHandle_returns_null_when_no_help_flag_is_present()
    {
        var @out = new StringWriter();

        Assert.Null(UsageCli.TryHandle(new[] { "--driver", "ansi", "some.txt" }, @out));
        Assert.Equal("", @out.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void TryHandle_prints_usage_and_exits_zero(string flag)
    {
        var @out = new StringWriter();

        var exit = UsageCli.TryHandle(new[] { flag }, @out);

        Assert.Equal(0, exit);
        Assert.Contains("Usage:", @out.ToString());
        Assert.Contains("tuicode [<path>]", @out.ToString());
    }

    [Fact]
    public void TryHandle_prints_usage_even_when_a_path_comes_first()
    {
        var @out = new StringWriter();

        Assert.Equal(0, UsageCli.TryHandle(new[] { "some.txt", "--help" }, @out));
    }

    [Fact]
    public void Usage_lists_every_flag_the_app_accepts()
    {
        var parsed = UsageCli.Flags
            .Concat(TerminalIntegrationCli.Flags)
            .Append(DriverSelection.Flag);

        foreach (var flag in parsed)
            Assert.Contains(flag, UsageCli.Text);
    }

    [Fact]
    public void Usage_gives_each_terminal_integration_flag_its_id_suffix()
    {
        foreach (var flag in TerminalIntegrationCli.Flags.Where(f => f != TerminalIntegrationCli.ListFlag))
            Assert.Contains($"{flag}[=<id>]", UsageCli.Text);
    }

    [Fact]
    public void Usage_names_every_driver_the_app_understands()
    {
        Assert.Contains("windows | dotnet | ansi", UsageCli.Text);
    }

    [Fact]
    public void Usage_points_at_the_repository()
    {
        Assert.Contains("https://github.com/mentaldesk/TuiCode", UsageCli.Text);
    }

    [Fact]
    public void Usage_fits_an_eighty_column_terminal()
    {
        var tooWide = UsageCli.Text
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 80);

        Assert.Empty(tooWide);
    }
}
