using TuiCode.Abstractions;

namespace TuiCode.Workbench.Configuration;

/// <summary>A file the command line named, and where in it the cursor goes (#265).</summary>
public sealed record StartupFile(IFileInfo File, FilePosition? Position = null);

/// <summary>What the command line asks the workbench to open (#263).</summary>
/// <param name="Workspace">The folder the explorer roots at; the current directory when nothing else applies.</param>
/// <param name="Files">The files to open as tabs, in the order given, the first one active (#266).</param>
/// <param name="Error">A message to print to stderr instead of booting, or null.</param>
/// <param name="Declined">A path wasn't there and the user said not to create it: exit quietly, don't boot.</param>
public sealed record StartupTarget(
    IDirectoryInfo? Workspace,
    IReadOnlyList<StartupFile> Files,
    string? Error = null,
    bool Declined = false);

/// <summary>Asked before <c>tuicode &lt;path&gt;</c> creates a path that isn't there (#263).</summary>
public delegate bool ConfirmCreate(string fullPath, bool directory);

/// <summary>
/// Resolves <c>tuicode &lt;path&gt;...</c> (#263, #266) into the folder to root at and the files to open.
/// Every positional argument is a path; a missing one is only created once the user agrees, by the rule
/// <c>Ctrl+N</c> uses (<see cref="FilePaths.IsDirectoryPath"/>): a trailing slash means a folder, anything
/// else a file, and intermediate folders are created. The first path decides the workspace: a folder
/// is it, and a file leaves the folder you ran from as it when it's under there, otherwise its own folder.
/// The rest just open as tabs, wherever they live; a folder among them opens nothing and doesn't re-root.
/// Each file may carry a position (<see cref="PathPosition"/>), which neither a folder argument nor a path
/// that had to be created does: the first has no cursor to place, and the second is created as typed,
/// position and all.
/// </summary>
/// <remarks>
/// Nothing opens unless every path resolves: each is asked about and created before any becomes a tab, so a
/// list with one bad path in it leaves you at the shell rather than in a half-opened session. The same path
/// twice is one path: it opens one tab, and you're only asked once about creating it.
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
        var paths = Paths(args);
        if (paths.Count == 0) return new StartupTarget(workspace, []);

        var requests = paths
            .Select(path => Classify(path, fileSystem, currentDirectory))
            .DistinctBy(request => request.FullPath, StringComparer.Ordinal)
            .ToList();

        // Ask about all of them before creating any, so declining the last leaves the earlier ones uncreated.
        if (requests.Any(request => request.Missing && !confirmCreate(request.FullPath, request.Directory)))
            return new StartupTarget(null, [], Declined: true);

        foreach (var request in requests.Where(request => request.Missing))
            if (Create(fileSystem, request) is { } error)
                return new StartupTarget(null, [], error);

        return new StartupTarget(Root(fileSystem, workspace, requests[0]), Tabs(fileSystem, requests));
    }

    private static List<string> Paths(IReadOnlyList<string> args)
    {
        var paths = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) paths.Add(arg.Trim());
            else if (arg == ValueFlag) i++;
        }
        return paths;
    }

    private sealed record Request(string FullPath, bool Directory, FilePosition? Position, bool Missing);

    private static Request Classify(string argument, IFileSystem fileSystem, string currentDirectory)
    {
        var (requested, position) = PathPosition.Split(argument, fileSystem, currentDirectory);
        var fullPath = Full(fileSystem, currentDirectory, requested);

        if (fileSystem.Directory.Exists(fullPath))
            return new Request(fullPath, Directory: true, Position: null, Missing: false);
        if (fileSystem.File.Exists(fullPath))
            return new Request(fullPath, Directory: false, position, Missing: false);

        // Nothing is there to put a cursor in, so what gets created is what was typed, colon and all (#265).
        return new Request(
            Full(fileSystem, currentDirectory, argument),
            FilePaths.IsDirectoryPath(argument),
            Position: null,
            Missing: true);
    }

    private static string? Create(IFileSystem fileSystem, Request request)
    {
        try
        {
            if (request.Directory)
            {
                fileSystem.Directory.CreateDirectory(request.FullPath);
                return null;
            }

            if (fileSystem.Path.GetDirectoryName(request.FullPath) is { Length: > 0 } parent)
                fileSystem.Directory.CreateDirectory(parent);
            fileSystem.File.Create(request.FullPath).Dispose();
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Describe(e, request.FullPath);
        }
    }

    private static IDirectoryInfo Root(IFileSystem fileSystem, IDirectoryInfo workspace, Request first)
    {
        if (first.Directory) return fileSystem.DirectoryInfo.New(first.FullPath);
        return FilePaths.IsSameOrUnder(first.FullPath, workspace.FullName)
            ? workspace
            : fileSystem.DirectoryInfo.New(fileSystem.Path.GetDirectoryName(first.FullPath)!);
    }

    private static List<StartupFile> Tabs(IFileSystem fileSystem, IEnumerable<Request> requests) => requests
        .Where(request => !request.Directory)
        .Select(request => new StartupFile(fileSystem.FileInfo.New(request.FullPath), request.Position))
        .ToList();

    private static string Full(IFileSystem fileSystem, string currentDirectory, string path) =>
        fileSystem.Path.GetFullPath(fileSystem.Path.Combine(currentDirectory, path));

    private static string Describe(Exception e, string path) => e is UnauthorizedAccessException
        ? $"{ProgramName}: permission denied: {path}"
        : $"{ProgramName}: cannot create {path}: {e.Message}";
}
