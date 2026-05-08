using System.Diagnostics;

namespace TuiCode.Workbench;

/// <summary>
/// Disables terminal software flow control (XON/XOFF) so that Ctrl+S and
/// Ctrl+Q reach the application instead of being intercepted by the tty
/// driver. Restores the original tty settings on dispose.
/// </summary>
internal sealed class TerminalFlowControl : IDisposable
{
    private readonly bool _supported;
    private readonly string? _savedSettings;
    private bool _disposed;

    public TerminalFlowControl()
    {
        _supported = !OperatingSystem.IsWindows() && Environment.UserInteractive;
        if (!_supported) return;

        try
        {
            _savedSettings = RunStty("-g");
            RunStty("-ixon -ixoff");
        }
        catch
        {
            // Don't fail startup over flow control — worst case Ctrl+S is
            // swallowed and the user can still use the app.
            _savedSettings = null;
        }
    }

    public void Dispose()
    {
        if (_disposed || !_supported || _savedSettings is null) return;
        _disposed = true;
        try { RunStty(_savedSettings); } catch { /* best effort */ }
    }

    private static string RunStty(string args)
    {
        var psi = new ProcessStartInfo("stty", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start stty");
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return stdout.Trim();
    }
}
