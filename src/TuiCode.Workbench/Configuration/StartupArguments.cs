using TuiCode.Abstractions;

namespace TuiCode.Workbench.Configuration;

/// <summary>What the command line asks the workbench to open (#263).</summary>
/// <param name="Workspace">The folder the explorer roots at; the current directory when nothing else applies.</param>
/// <param name="File">The file to open in the editor, or null for a folder-only start.</param>
/// <param name="Error">A message to print to stderr instead of booting, or null.</param>
/// <param name="Declined">The path wasn't there and the user said not to create it: exit quietly, don't boot.</param>
/// <param name="Position">Where to put the cursor in <paramref name="File"/> (#265), or null for line 1.</param>
public sealed record StartupTarget(
    IDirectoryInfo? Workspace,
    IFileInfo? File,
    string? Error = null,
    bool Declined = false,
    FilePosition? Position = null);

/// <summary>Asked before <c>tuicode &lt;path&gt;</c> creates a path that isn't there (#263).</summary>
public delegate bool ConfirmCreate(string fullPath, bool directory);

/// <summary>
/// Resolves <c>tuicode &lt;path&gt;</c> (#263) into the folder to root at and the file to open.
/// The first positional argument is a path; a missing one is only created once the user agrees,
/// by the rule <c>Ctrl+N</c> uses (<see cref="FilePaths.IsDirectoryPath"/>): a trailing slash means
/// a folder, anything else a file, and intermediate folders are created. The workspace is the folder
/// you ran from when it contains that file, and the file's own folder when it doesn't. A file may carry
/// a position (<see cref="PathPosition"/>), which a folder argument doesn't: there's no cursor to place.
/// </summary>
/// <remarks>
/// Flags are never paths. <c>--driver</c> takes a value, so the argument after it is skipped with
/// it; anything else starting with <c>--</c> belongs to another parser (<see cref="DriverSelection"/>,
/// <c>TerminalIntegrationCli</c>, <c>--smoke</c>) and is passed over.
/// </remarks>
public static class StartupArguments
{
    private const string ProgramName = "tuicode";

    // The only flag whose value is a separate argument, and so could be mistaken for a path.
    private const string ValueFlag = "--driver";

    public static StartupTarget Resolve(
        IReadOnlyList<string> args,
        IFileSystem fileSystem,
        string currentDirectory,
        ConfirmCreate confirmCreate)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(currentDirectory);
        ArgumentNullException.ThrowIfNull(confirmCreate);

        var workspace = fileSystem.DirectoryInfo.New(currentDirectory);
        if (FirstPath(args) is not { } argument) return new StartupTarget(workspace, null);

        var (requested, position) = PathPosition.Split(argument, fileSystem, currentDirectory);
        var fullPath = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(currentDirectory, requested));

        if (fileSystem.Directory.Exists(fullPath))
            return new StartupTarget(fileSystem.DirectoryInfo.New(fullPath), null);
        if (fileSystem.File.Exists(fullPath))
            return ForFile(fileSystem, workspace, fullPath, position);

        var directory = FilePaths.IsDirectoryPath(requested);
        if (!confirmCreate(fullPath, directory))
            return new StartupTarget(null, null, Declined: true);

        try
        {
            if (directory)
            {
                fileSystem.Directory.CreateDirectory(fullPath);
                return new StartupTarget(fileSystem.DirectoryInfo.New(fullPath), null);
            }

            if (fileSystem.Path.GetDirectoryName(fullPath) is { Length: > 0 } parent)
                fileSystem.Directory.CreateDirectory(parent);
            fileSystem.File.Create(fullPath).Dispose();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new StartupTarget(null, null, Describe(e, fullPath));
        }

        return ForFile(fileSystem, workspace, fullPath, position);
    }

    private static string? FirstPath(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) return arg.Trim();
            if (arg == ValueFlag) i++;
        }
        return null;
    }

    private static StartupTarget ForFile(
        IFileSystem fileSystem, IDirectoryInfo workspace, string fullPath, FilePosition? position)
    {
        var root = FilePaths.IsSameOrUnder(fullPath, workspace.FullName)
            ? workspace
            : fileSystem.DirectoryInfo.New(fileSystem.Path.GetDirectoryName(fullPath)!);
        return new StartupTarget(root, fileSystem.FileInfo.New(fullPath), Position: position);
    }

    private static string Describe(Exception e, string path) => e is UnauthorizedAccessException
        ? $"{ProgramName}: permission denied: {path}"
        : $"{ProgramName}: cannot create {path}: {e.Message}";
}
