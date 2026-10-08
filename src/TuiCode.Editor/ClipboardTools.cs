using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TuiCode.Editor;

/// <summary>A platform's own clipboard program, e.g. <c>pbcopy</c>, and the one that reads the clipboard back.</summary>
internal sealed record ClipboardTool(string Name, string[] Copy, string[] Paste, Encoding InputEncoding)
{
    /// <summary>Variables set for both programs on top of TuiCode's own environment.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

internal abstract record ToolRun
{
    public sealed record NotFound : ToolRun;

    public sealed record TimedOut : ToolRun;

    public sealed record Exited(int Code, string Output, string Error) : ToolRun;
}

internal interface IProcessRunner
{
    ToolRun Run(string[] command, IReadOnlyDictionary<string, string> environment, byte[] input, bool readOutput, TimeSpan timeout);
}

/// <summary>Copies through the platform's clipboard program when Terminal.Gui's clipboard didn't take the text (#319).</summary>
internal sealed class ClipboardTools(IReadOnlyList<ClipboardTool> candidates, string installHint, IProcessRunner runner)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // Without a UTF-8 locale pbcopy stores the bytes as Mac Roman and pbpaste undoes it, so the read-back can't tell (#347).
    private static readonly ClipboardTool PbCopy = new("pbcopy", ["pbcopy"], ["pbpaste"], new UTF8Encoding(false))
    {
        Environment = new Dictionary<string, string> { ["LC_ALL"] = "en_US.UTF-8" },
    };
    private static readonly ClipboardTool WlCopy = new("wl-copy", ["wl-copy"], ["wl-paste", "--no-newline"], new UTF8Encoding(false));
    private static readonly ClipboardTool XClip = new("xclip", ["xclip", "-selection", "clipboard"], ["xclip", "-selection", "clipboard", "-o"], new UTF8Encoding(false));
    // clip.exe reads UTF-16 only when it starts with a byte order mark; otherwise it uses the console code page.
    private static readonly ClipboardTool Clip = new("clip.exe", ["clip.exe"],
        ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
            "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); [Console]::Out.Write((Get-Clipboard -Raw))"],
        new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

    // Declared after the tools: static initializers run in textual order.
    public static ClipboardTools ThisMachine { get; } = For(
        OperatingSystem.IsMacOS(), OperatingSystem.IsWindows(),
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")), new ProcessRunner());

    public static ClipboardTools For(bool macOS, bool windows, bool wayland, IProcessRunner runner) =>
        macOS ? new([PbCopy], "pbcopy", runner)
        : windows ? new([Clip], "clip.exe", runner)
        : new(wayland ? [WlCopy, XClip] : [XClip], "wl-copy or xclip", runner);

    public IReadOnlyList<ClipboardTool> Candidates => candidates;

    public CopyOutcome.Failed? Write(string text)
    {
        foreach (var tool in candidates)
        {
            var input = tool.InputEncoding.GetPreamble().Concat(tool.InputEncoding.GetBytes(text)).ToArray();
            var copied = runner.Run(tool.Copy, tool.Environment, input, readOutput: false, Timeout);
            if (copied is ToolRun.NotFound) continue;
            if (Failure(tool, copied) is { } copyFailed) return copyFailed;

            var pasted = runner.Run(tool.Paste, tool.Environment, [], readOutput: true, Timeout);
            if (pasted is ToolRun.NotFound) return new CopyOutcome.Failed($"{tool.Paste[0]} not found ({tool.Name})");
            if (Failure(tool, pasted) is { } pasteFailed) return pasteFailed;
            return ((ToolRun.Exited)pasted).Output.ReplaceLineEndings("\n") == text.ReplaceLineEndings("\n")
                ? null
                : new CopyOutcome.Failed($"the clipboard didn't take the text ({tool.Name})");
        }
        return new CopyOutcome.Failed($"no clipboard tool found (install {installHint})");
    }

    private static CopyOutcome.Failed? Failure(ClipboardTool tool, ToolRun run) => run switch
    {
        ToolRun.TimedOut => new($"no answer after {Timeout.TotalSeconds:0} seconds ({tool.Name})"),
        ToolRun.Exited { Code: not 0 } exited => new($"{FirstLine(exited.Error) ?? $"exited with code {exited.Code}"} ({tool.Name})"),
        _ => null,
    };

    private static string? FirstLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
}

internal sealed class ProcessRunner : IProcessRunner
{
    public ToolRun Run(string[] command, IReadOnlyDictionary<string, string> environment, byte[] input, bool readOutput, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in command.Skip(1)) info.ArgumentList.Add(argument);
        Override(info.Environment, environment);

        Process process;
        try
        {
            process = Process.Start(info)!;
        }
        catch (Win32Exception)
        {
            return new ToolRun.NotFound();
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            _ = Task.Run(() =>
            {
                try
                {
                    process.StandardInput.BaseStream.Write(input);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                }
            });

            var started = Stopwatch.StartNew();
            if (!process.WaitForExit(timeout))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
                return new ToolRun.TimedOut();
            }
            // wl-copy and xclip leave a background copy serving the clipboard, holding the pipes open.
            if (process.ExitCode == 0 && !readOutput) return new ToolRun.Exited(0, "", "");
            var remaining = timeout - started.Elapsed;
            if (!Task.WaitAll([output, error], remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero))
                return new ToolRun.TimedOut();
            return new ToolRun.Exited(process.ExitCode, output.Result, error.Result);
        }
    }

    internal static void Override(IDictionary<string, string?> inherited, IReadOnlyDictionary<string, string> environment)
    {
        foreach (var (name, value) in environment) inherited[name] = value;
    }
}
