using System.ComponentModel;
using System.Diagnostics;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Navigation;

internal interface IBrowser
{
    /// <summary>Opens <paramref name="url"/>; returns why it couldn't, or null.</summary>
    string? Open(string url);
}

/// <summary>The machine's default browser, through <c>open</c>, <c>xdg-open</c> or the Windows shell.</summary>
internal sealed class SystemBrowser(string? program) : IBrowser
{
    private const int FileNotFound = 2;

    public static SystemBrowser For(IEnvironment environment) =>
        new(environment.IsWindows ? null : environment.IsMacOS ? "open" : "xdg-open");

    public string? Open(string url)
    {
        // Redirected so the opener can't write over the screen.
        var start = program is null
            ? new ProcessStartInfo(url) { UseShellExecute = true }
            : new ProcessStartInfo(program, [url])
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
        try
        {
            var process = Process.Start(start);
            if (process is null || program is null) return null;
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == FileNotFound && program is not null)
        {
            return $"{program} not found";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
