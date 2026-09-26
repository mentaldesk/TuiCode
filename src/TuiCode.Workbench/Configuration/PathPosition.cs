using TuiCode.Abstractions;

namespace TuiCode.Workbench.Configuration;

/// <summary>A 1-based cursor position copied off a command line, as every tool prints them (#265).</summary>
public readonly record struct FilePosition(int Line, int Column);

/// <summary>
/// Splits <c>path:line[:column]</c> — the shape <c>grep -n</c>, stack traces and CI logs print — into the
/// path and where to put the cursor (#265). A suffix counts as a position only when it is all digits, so a
/// file whose name really ends in <c>:abc</c> or <c>:-1</c> keeps it, and a Windows drive letter is never
/// mistaken for one. A path that exists on disk wins outright: <c>weird:42</c> opens as itself if it's there.
/// </summary>
public static class PathPosition
{
    /// <param name="argument">The positional argument, as typed.</param>
    /// <returns>The path to open, and the position, or null when the argument carries none.</returns>
    public static (string Path, FilePosition? Position) Split(
        string argument,
        IFileSystem fileSystem,
        string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(argument);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(currentDirectory);

        if (Exists(argument, fileSystem, currentDirectory)) return (argument, null);

        // `grep -n` prints `path:line:`; the empty last field is part of its format, not a column.
        var path = argument.EndsWith(':') ? argument[..^1] : argument;

        if (TakeNumber(path) is not { } last) return (argument, null);
        if (TakeNumber(last.Head) is { } first) return (first.Head, Position(first.Number, last.Number));
        return (last.Head, Position(last.Number, 1));
    }

    /// <summary>A line of 0 or less is no position at all, not an error: the cursor stays where it opened.</summary>
    private static FilePosition? Position(int line, int column) =>
        line < 1 ? null : new FilePosition(line, Math.Max(column, 1));

    private static (string Head, int Number)? TakeNumber(string path)
    {
        var colon = path.LastIndexOf(':');
        if (colon <= 0) return null;
        var suffix = path[(colon + 1)..];
        if (suffix.Length == 0 || !suffix.All(char.IsAsciiDigit)) return null;
        if (!int.TryParse(suffix, out var number)) return null;
        // `C:` is a drive, not a path with a position on it.
        var head = path[..colon];
        return head.Length == 1 && char.IsAsciiLetter(head[0]) ? null : (head, number);
    }

    private static bool Exists(string path, IFileSystem fileSystem, string currentDirectory)
    {
        string full;
        try { full = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(currentDirectory, path)); }
        catch (ArgumentException) { return false; }
        return fileSystem.File.Exists(full) || fileSystem.Directory.Exists(full);
    }
}
