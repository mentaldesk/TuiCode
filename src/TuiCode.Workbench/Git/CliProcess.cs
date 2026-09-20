using System.ComponentModel;
using System.Diagnostics;

namespace TuiCode.Workbench.Git;

/// <summary>Runs a CLI to completion, turning a missing executable or a timeout into a message.</summary>
internal static class CliProcess
{
    public static async Task<CliRun> RunAsync(string name, ProcessStartInfo info, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return new CliRun(-1, "", "", $"{name} isn't installed or isn't on PATH", Missing: true);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var error = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return new CliRun(process.ExitCode, await output, await error, null);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            cancellationToken.ThrowIfCancellationRequested();
            return CliRun.Failed($"{name} didn't answer within {timeout.TotalSeconds:0.#} s");
        }
    }
}

internal readonly record struct CliRun(int ExitCode, string Output, string Error, string? Failure, bool Missing = false)
{
    public static CliRun Failed(string failure) => new(-1, "", "", failure);
}
