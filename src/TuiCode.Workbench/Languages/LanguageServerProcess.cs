using System.ComponentModel;
using System.Diagnostics;

namespace TuiCode.Workbench.Languages;

/// <summary>A running language server: what it writes, what it reads, and a way to end it.</summary>
public interface ILanguageServerProcess : IDisposable
{
    Stream Output { get; }
    Stream Input { get; }
    Task Exited { get; }
    void Kill();
}

public interface ILanguageServerLauncher
{
    /// <summary>Starts <paramref name="command"/> in <paramref name="directory"/>; null when it isn't installed.</summary>
    ILanguageServerProcess? Launch(string command, IReadOnlyList<string> arguments, string directory);
}

public sealed class ProcessLauncher : ILanguageServerLauncher
{
    public ILanguageServerProcess? Launch(string command, IReadOnlyList<string> arguments, string directory)
    {
        var info = new ProcessStartInfo(command)
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            process.Dispose();
            return null;
        }
        // Unread, a full stderr pipe would stall the server.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return new RunningProcess(process);
    }

    private sealed class RunningProcess(Process process) : ILanguageServerProcess
    {
        public Stream Output => process.StandardOutput.BaseStream;
        public Stream Input => process.StandardInput.BaseStream;
        public Task Exited { get; } = process.WaitForExitAsync();

        public void Kill()
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
            }
        }

        public void Dispose() => process.Dispose();
    }
}
