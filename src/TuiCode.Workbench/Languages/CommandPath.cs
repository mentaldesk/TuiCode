using System.IO.Abstractions;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Languages;

/// <summary>Whether a command would start: a path to a file, or a name found on <c>PATH</c>.</summary>
public static class CommandPath
{
    public static bool Exists(string command, IFileSystem fs, IEnvironment environment)
    {
        command = command.Trim();
        if (command.Length == 0) return false;
        var extensions = environment.IsWindows
            ? ["", .. (environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)]
            : new[] { "" };
        if (command.Contains('/') || (environment.IsWindows && command.Contains('\\')))
            return extensions.Any(extension => fs.File.Exists(command + extension));
        var separator = environment.IsWindows ? ';' : ':';
        var folders = (environment.GetEnvironmentVariable("PATH") ?? "").Split(separator, StringSplitOptions.RemoveEmptyEntries);
        return folders.Any(folder => extensions.Any(extension => fs.File.Exists(fs.Path.Combine(folder, command + extension))));
    }
}
