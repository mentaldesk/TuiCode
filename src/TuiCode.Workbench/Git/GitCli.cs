using System.Diagnostics;
using System.Globalization;
using System.Text;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Git;

public sealed class GitCli(IFileSystem fileSystem, string executable = "git", TimeSpan? timeout = null) : IGitCli
{
    internal const int HistoryLimit = 200;

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(5);

    public async Task<GitResult<string?>> GetRepoRootAsync(string path, CancellationToken cancellationToken = default)
    {
        var run = await RunAsync(DirectoryOf(path), ["rev-parse", "--show-toplevel"], cancellationToken);
        if (run.Failure is { } failure)
            return GitResult<string?>.Failure(failure);
        if (run.ExitCode == 0)
            return GitResult<string?>.Success(fileSystem.Path.GetFullPath(run.Output.TrimEnd('\r', '\n')));
        return run.Error.Contains("not a git repository", StringComparison.Ordinal)
            ? GitResult<string?>.Success(null)
            : GitResult<string?>.Failure(ErrorMessage(run));
    }

    public async Task<GitResult<string?>> ShowFileAsync(string filePath, string revision, CancellationToken cancellationToken = default)
    {
        if (!IsSafeRevision(revision))
            return GitResult<string?>.Failure($"Unknown revision '{revision}'");

        // A `./` path is relative to -C, so git works out the root-relative path even through symlinks.
        return await ShowAsync(DirectoryOf(filePath), $"./{fileSystem.Path.GetFileName(filePath)}", revision, cancellationToken);
    }

    public Task<GitResult<string?>> ShowRepoFileAsync(string repoRoot, string repoPath, string revision, CancellationToken cancellationToken = default) =>
        IsSafeRevision(revision)
            ? ShowAsync(repoRoot, repoPath, revision, cancellationToken)
            : Task.FromResult(GitResult<string?>.Failure($"Unknown revision '{revision}'"));

    private async Task<GitResult<string?>> ShowAsync(string directory, string path, string revision, CancellationToken cancellationToken)
    {
        var run = await RunAsync(directory, ["show", $"{revision}:{path}"], cancellationToken);
        if (run.Failure is { } failure)
            return GitResult<string?>.Failure(failure);
        if (run.ExitCode == 0)
            return GitResult<string?>.Success(run.Output);

        var resolves = await ResolvesAsync(directory, revision, cancellationToken);
        if (!resolves.Succeeded)
            return GitResult<string?>.Failure(resolves.Error!);
        return resolves.Value
            ? GitResult<string?>.Success(null)
            : GitResult<string?>.Failure($"Unknown revision '{revision}'");
    }

    public async Task<GitResult<IReadOnlyList<GitRef>>> GetRefsAsync(string path, CancellationToken cancellationToken = default)
    {
        var run = await RunAsync(DirectoryOf(path),
            ["for-each-ref", "--format=%(refname)%00%(refname:short)%00%(symref)", "refs/heads", "refs/remotes", "refs/tags"],
            cancellationToken);
        return run.Failure is null && run.ExitCode == 0
            ? GitResult<IReadOnlyList<GitRef>>.Success(ParseRefs(run.Output))
            : GitResult<IReadOnlyList<GitRef>>.Failure(ErrorMessage(run));
    }

    public async Task<GitResult<IReadOnlyList<GitCommit>>> GetFileHistoryAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var run = await RunAsync(DirectoryOf(filePath),
            ["-c", "i18n.logOutputEncoding=UTF-8", "log", $"-n{HistoryLimit}", "--no-show-signature",
             "--format=%h%x00%aI%x00%s", "--", fileSystem.Path.GetFileName(filePath)],
            cancellationToken);
        return run.Failure is null && run.ExitCode == 0
            ? GitResult<IReadOnlyList<GitCommit>>.Success(ParseLog(run.Output))
            : GitResult<IReadOnlyList<GitCommit>>.Failure(ErrorMessage(run));
    }

    public async Task<GitResult<bool>> ResolvesAsync(string path, string revision, CancellationToken cancellationToken = default)
    {
        if (!IsSafeRevision(revision))
            return GitResult<bool>.Success(false);

        var run = await RunAsync(DirectoryOf(path), ["rev-parse", "--verify", "--quiet", $"{revision}^{{commit}}"], cancellationToken);
        return run.Failure is null && run.ExitCode is 0 or 1
            ? GitResult<bool>.Success(run.ExitCode == 0)
            : GitResult<bool>.Failure(ErrorMessage(run));
    }

    public async Task<GitResult<string?>> GetCurrentBranchAsync(string path, CancellationToken cancellationToken = default)
    {
        var run = await RunAsync(DirectoryOf(path), ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);
        return run.Failure is null && run.ExitCode is 0 or 1
            ? GitResult<string?>.Success(run.ExitCode == 0 ? run.Output.Trim() : null)
            : GitResult<string?>.Failure(ErrorMessage(run));
    }

    public async Task<GitResult<string?>> GetDefaultBranchAsync(string path, CancellationToken cancellationToken = default)
    {
        var directory = DirectoryOf(path);
        var run = await RunAsync(directory, ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], cancellationToken);
        if (run.Failure is not null || run.ExitCode is not (0 or 1))
            return GitResult<string?>.Failure(ErrorMessage(run));
        if (run.ExitCode == 0)
            return GitResult<string?>.Success(run.Output.Trim());

        foreach (var branch in (string[])["main", "master"])
        {
            var resolves = await ResolvesAsync(directory, branch, cancellationToken);
            if (!resolves.Succeeded)
                return GitResult<string?>.Failure(resolves.Error!);
            if (resolves.Value)
                return GitResult<string?>.Success(branch);
        }
        return GitResult<string?>.Success(null);
    }

    public async Task<GitResult<string?>> GetMergeBaseAsync(string path, string first, string second, CancellationToken cancellationToken = default)
    {
        foreach (var revision in (string[])[first, second])
            if (!IsSafeRevision(revision))
                return GitResult<string?>.Failure($"Unknown revision '{revision}'");

        var run = await RunAsync(DirectoryOf(path), ["merge-base", first, second], cancellationToken);
        return run.Failure is null && run.ExitCode is 0 or 1
            ? GitResult<string?>.Success(run.ExitCode == 0 ? run.Output.Trim() : null)
            : GitResult<string?>.Failure(ErrorMessage(run));
    }

    public async Task<GitResult<IReadOnlyList<GitChange>>> GetChangedFilesAsync(string path, string revision, CancellationToken cancellationToken = default)
    {
        if (!IsSafeRevision(revision))
            return GitResult<IReadOnlyList<GitChange>>.Failure($"Unknown revision '{revision}'");

        var run = await RunAsync(DirectoryOf(path),
            ["diff", "--name-status", "-z", "-M", "--no-ext-diff", revision, "--"], cancellationToken);
        return run.Failure is null && run.ExitCode == 0
            ? GitResult<IReadOnlyList<GitChange>>.Success(ParseChanges(run.Output))
            : GitResult<IReadOnlyList<GitChange>>.Failure(ErrorMessage(run));
    }

    /// <summary>Reads <c>--name-status -z</c>: a status, then one path, or two for a rename or copy. A copy counts as added.</summary>
    internal static IReadOnlyList<GitChange> ParseChanges(string output)
    {
        var fields = output.Split('\0');
        var changes = new List<GitChange>();
        for (var i = 0; i + 1 < fields.Length; i += 2)
        {
            var status = fields[i];
            if (status.Length == 0)
                break;
            if (status[0] is 'R' or 'C' && i + 2 < fields.Length)
            {
                changes.Add(status[0] == 'R'
                    ? new GitChange(GitChangeKind.Renamed, fields[i + 2], fields[i + 1])
                    : new GitChange(GitChangeKind.Added, fields[i + 2]));
                i++;
                continue;
            }
            var kind = status[0] switch
            {
                'A' => GitChangeKind.Added,
                'D' => GitChangeKind.Deleted,
                _ => GitChangeKind.Modified,
            };
            changes.Add(new GitChange(kind, fields[i + 1]));
        }
        return changes;
    }

    internal static IReadOnlyList<GitRef> ParseRefs(string output)
    {
        var refs = new List<GitRef>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd('\r').Split('\0');
            if (fields.Length < 3 || fields[2].Length > 0)
                continue;
            GitRefKind? kind = fields[0] switch
            {
                var r when r.StartsWith("refs/heads/", StringComparison.Ordinal) => GitRefKind.Branch,
                var r when r.StartsWith("refs/remotes/", StringComparison.Ordinal) => GitRefKind.RemoteBranch,
                var r when r.StartsWith("refs/tags/", StringComparison.Ordinal) => GitRefKind.Tag,
                _ => null,
            };
            if (kind is { } k)
                refs.Add(new GitRef(fields[1], k));
        }
        return refs.OrderBy(r => r.Kind).ToList();
    }

    internal static IReadOnlyList<GitCommit> ParseLog(string output)
    {
        var commits = new List<GitCommit>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd('\r').Split('\0', 3);
            if (fields.Length == 3
                && DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                commits.Add(new GitCommit(fields[0], fields[2], date));
        }
        return commits;
    }

    private string DirectoryOf(string path) =>
        fileSystem.Directory.Exists(path) ? path : fileSystem.Path.GetDirectoryName(path) ?? path;

    private static bool IsSafeRevision(string revision) =>
        !string.IsNullOrWhiteSpace(revision) && !revision.StartsWith('-');

    private static string ErrorMessage(CliRun run)
    {
        if (run.Failure is { } failure)
            return failure;
        var line = run.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (line is null)
            return $"git exited with code {run.ExitCode}";
        return line.StartsWith("fatal: ", StringComparison.Ordinal) ? line["fatal: ".Length..] : line;
    }

    private Task<CliRun> RunAsync(string workingDirectory, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(workingDirectory);
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        // English messages, so "not a git repository" can be recognised.
        info.Environment["LC_ALL"] = "C";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        return CliProcess.RunAsync("git", info, _timeout, cancellationToken);
    }
}
