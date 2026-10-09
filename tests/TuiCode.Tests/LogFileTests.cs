using Microsoft.Extensions.Logging;
using TuiCode.Workbench.Logging;

namespace TuiCode.Tests;

public class LogFileTests
{
    private const string Directory = "/home/.tui/logs";
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 12, 3, TimeSpan.Zero);

    private readonly MockFileSystem _fs = new();

    private LogFile Log() => new(_fs, Directory, () => Now);

    private string Read(string name) => _fs.File.ReadAllText(_fs.Path.Combine(Directory, name));

    [Fact]
    public void Warnings_and_errors_are_written_a_line_each()
    {
        var logger = Log().CreateLogger("test");

        logger.LogWarning("Not watching {Folder}", "/work");
        logger.LogError("Something broke");

        Assert.Equal(
            "2026-10-04 09:12:03 WARN  Not watching /work\n2026-10-04 09:12:03 ERROR Something broke\n",
            Read(LogFile.FileName));
    }

    [Fact]
    public void An_exception_adds_its_message_and_its_stack_trace_indented()
    {
        Exception thrown;
        try
        {
            throw new IOException("disk full");
        }
        catch (IOException e)
        {
            thrown = e;
        }

        Log().CreateLogger("test").LogCritical(thrown, "TuiCode crashed");

        var lines = Read(LogFile.FileName).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("2026-10-04 09:12:03 CRIT  TuiCode crashed: disk full", lines[0]);
        Assert.StartsWith("    at TuiCode.Tests.LogFileTests.", lines[1]);
    }

    [Fact]
    public void Information_and_debug_are_not_written_or_counted()
    {
        var log = Log();
        var logger = log.CreateLogger("test");

        logger.LogInformation("Opened");
        logger.LogDebug("Detail");

        Assert.False(_fs.File.Exists(log.Path));
        Assert.Equal(0, log.Unseen);
    }

    [Fact]
    public void Starting_keeps_the_last_session_as_the_previous_log_and_drops_the_one_before()
    {
        _fs.AddFile(_fs.Path.Combine(Directory, LogFile.FileName), new MockFileData("last session\n"));
        _fs.AddFile(_fs.Path.Combine(Directory, LogFile.PreviousFileName), new MockFileData("the one before\n"));

        Log().Start();

        Assert.Equal("", Read(LogFile.FileName));
        Assert.Equal("last session\n", Read(LogFile.PreviousFileName));
    }

    [Fact]
    public void The_first_warning_starts_the_session_once()
    {
        _fs.AddFile(_fs.Path.Combine(Directory, LogFile.FileName), new MockFileData("last session\n"));
        var log = Log();
        var logger = log.CreateLogger("test");

        logger.LogWarning("one");
        log.Start();
        logger.LogWarning("two");

        Assert.Equal("2026-10-04 09:12:03 WARN  one\n2026-10-04 09:12:03 WARN  two\n", Read(LogFile.FileName));
        Assert.Equal("last session\n", Read(LogFile.PreviousFileName));
    }

    [Fact]
    public void Unseen_counts_each_warning_clears_when_seen_and_counts_again_after()
    {
        var log = Log();
        var logger = log.CreateLogger("test");

        logger.LogWarning("one");
        logger.LogWarning("two");
        var before = log.Unseen;
        log.MarkSeen();
        var seen = log.Unseen;
        logger.LogWarning("three");

        Assert.Equal((2, 0, 1), (before, seen, log.Unseen));
    }
}
