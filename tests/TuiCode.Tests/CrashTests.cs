using Microsoft.Extensions.Logging;
using TuiCode.Workbench.Logging;

namespace TuiCode.Tests;

public class CrashTests
{
    [Fact]
    public void A_crash_is_logged_and_the_log_named_after_the_terminal_is_restored()
    {
        var logger = new ListLogger<CrashTests>();
        var error = new StringWriter();
        List<string> order = [];

        var exit = Crash.Report(
            () => throw new InvalidOperationException("boom"),
            () => order.Add($"restored with {logger.Entries.Count} logged and \"{error}\" printed"),
            logger, error, "~/.tui/logs/TuiCode.log");

        Assert.Equal(1, exit);
        Assert.Equal([(LogLevel.Critical, "TuiCode crashed")], logger.Entries);
        Assert.Equal(["restored with 1 logged and \"\" printed"], order);
        Assert.Equal("TuiCode crashed. Details: ~/.tui/logs/TuiCode.log" + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void A_clean_run_logs_and_prints_nothing()
    {
        var logger = new ListLogger<CrashTests>();
        var error = new StringWriter();
        var restored = false;

        var exit = Crash.Report(() => { }, () => restored = true, logger, error, "log");

        Assert.Equal((0, false, ""), (exit, restored, error.ToString()));
        Assert.Empty(logger.Entries);
    }
}
