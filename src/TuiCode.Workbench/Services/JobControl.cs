using System.Runtime.InteropServices;

namespace TuiCode.Workbench.Services;

internal static class JobControl
{
    /// <summary>
    /// A job-control shell runs each job in a process group of its own; without one TuiCode shares the session
    /// leader's group, and nothing would ever send the <c>SIGCONT</c> that resumes it.
    /// </summary>
    public static bool IsAvailable()
    {
        if (OperatingSystem.IsWindows()) return false;
        try
        {
            var group = getpgrp();
            return group > 0 && group != getsid(0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("libc")]
    private static extern int getpgrp();

    [DllImport("libc")]
    private static extern int getsid(int pid);
}
