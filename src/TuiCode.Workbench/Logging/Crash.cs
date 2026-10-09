using Microsoft.Extensions.Logging;

namespace TuiCode.Workbench.Logging;

public static class Crash
{
    /// <summary>
    /// Runs <paramref name="run"/>. If it throws, logs the exception, then <paramref name="restore"/>s the terminal
    /// before saying where the details are, so the message isn't drawn over.
    /// </summary>
    public static int Report(Action run, Action restore, ILogger logger, TextWriter error, string logPath)
    {
        try
        {
            run();
            return 0;
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "TuiCode crashed");
            restore();
            error.WriteLine($"TuiCode crashed. Details: {logPath}");
            return 1;
        }
    }
}
