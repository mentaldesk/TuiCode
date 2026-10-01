using System.Diagnostics;
using System.Globalization;
using System.Text;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Git;

public sealed class GitCli(IFileSystem fileSystem, string executable = "git", TimeSpan? timeout = null) : IGitCli
{
    internal const int HistoryLimit = 200;

    /// <summary>Writing out a worktree copies a whole checkout, so it gets far longer than a query.</summary>
    private static readonly TimeSpan CheckoutTimeout = TimeSpan.FromMinutes(2);

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

    public async Task<GitResult<bool>> AddWorktreeAsync(string repoRoot, string worktreePath, CancellationToken cancellationToken = default)
    {
        if (fileSystem.Directory.Exists(worktreePath))
        {
            var listed = await RunAsync(repoRoot, ["worktree", "list", "--porcelain"], cancellationToken);
            if (listed.Failure is not null || listed.ExitCode != 0)
                return GitResult<bool>.Failure(ErrorMessage(listed));

            if (await WorktreeRootAsync(worktreePath, cancellationToken) is { } existing
                && ParseWorktrees(listed.Output).Any(w => string.Equals(w.Path, existing, StringComparison.Ordinal)))
                return GitResult<bool>.Success(false);
        }

        // Detached, so the PR's branch is free for gh to check out and no branch is created for a path we may reuse.
        var added = await RunAsync(repoRoot, ["worktree", "add", "--detach", worktreePath], cancellationToken, CheckoutTimeout);
        return added.Failure is null && added.ExitCode == 0
            ? GitResult<bool>.Success(true)
            : GitResult<bool>.Failure(ErrorMessage(added));
    }

    public async Task<GitResult<string?>> FindWorktreeAsync(string repoRoot, string branch, CancellationToken cancellationToken = default)
    {
        var listed = await RunAsync(repoRoot, ["worktree", "list", "--porcelain"], cancellationToken);
        if (listed.Failure is not null || listed.ExitCode != 0)
            return GitResult<string?>.Failure(ErrorMessage(listed));

        return GitResult<string?>.Success(ParseWorktrees(listed.Output)
            .FirstOrDefault(w => string.Equals(w.Branch, branch, StringComparison.Ordinal))?.Path);
    }

    public async Task<GitResult<IReadOnlyList<GitWorktree>>> GetWorktreesAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        var listed = await RunAsync(repoRoot, ["worktree", "list", "--porcelain"], cancellationToken);
        if (listed.Failure is not null || listed.ExitCode != 0)
            return GitResult<IReadOnlyList<GitWorktree>>.Failure(ErrorMessage(listed));

        return GitResult<IReadOnlyList<GitWorktree>>.Success(
            [.. ParseWorktrees(listed.Output).Select(w => w with { Path = fileSystem.Path.GetFullPath(w.Path) })]);
    }

    public async Task<GitResult<GitBlameLine?>> BlameAsync(string filePath, int line, string? contents = null, CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["-c", "i18n.logOutputEncoding=UTF-8", "-c", "core.quotePath=false", "blame", "--line-porcelain", "-L", $"{line},{line}"];
        if (contents is not null)
            arguments.AddRange(["--contents", "-"]);
        arguments.AddRange(["--", $"./{fileSystem.Path.GetFileName(filePath)}"]);

        var run = await RunAsync(DirectoryOf(filePath), arguments, cancellationToken, input: contents);
        if (run.Failure is { } failure)
            return GitResult<GitBlameLine?>.Failure(failure);
        if (run.ExitCode == 0 && ParseBlame(run.Output) is { } blamed)
            return GitResult<GitBlameLine?>.Success(blamed);
        return run.Error.Contains("no such path", StringComparison.Ordinal)
            ? GitResult<GitBlameLine?>.Success(null)
            : GitResult<GitBlameLine?>.Failure(ErrorMessage(run));
    }

    /// <summary>Reads one line of <c>blame --line-porcelain</c>: the hash, <c>key value</c> headers, then the line after a tab.</summary>
    internal static GitBlameLine? ParseBlame(string output)
    {
        var lines = output.Split('\n');
        var hash = lines[0].Split(' ')[0];
        if (hash.Length < 40)
            return null;

        string author = "", subject = "", path = "";
        GitBlamePrevious? previous = null;
        long seconds = 0;
        var offset = TimeSpan.Zero;
        foreach (var line in lines.Skip(1).Select(l => l.TrimEnd('\r')))
        {
            if (line.StartsWith('\t'))
                return new GitBlameLine(
                    hash, author, DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(offset), subject, line[1..], path, previous);

            var space = line.IndexOf(' ');
            if (space < 0)
                continue;
            var value = line[(space + 1)..];
            switch (line[..space])
            {
                case "author": author = value; break;
                case "author-time": long.TryParse(value, CultureInfo.InvariantCulture, out seconds); break;
                case "author-tz": offset = ParseTimeZone(value); break;
                case "summary": subject = value; break;
                case "filename": path = value; break;
                case "previous" when value.IndexOf(' ') is > 0 and var split:
                    previous = new GitBlamePrevious(value[..split], value[(split + 1)..]);
                    break;
            }
        }
        return null;
    }

    /// <summary>A <c>+1300</c> / <c>-0530</c> offset; anything else is UTC.</summary>
    private static TimeSpan ParseTimeZone(string value)
    {
        if (value.Length != 5 || value[0] is not ('+' or '-')
            || !int.TryParse(value.AsSpan(1, 2), CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(value.AsSpan(3, 2), CultureInfo.InvariantCulture, out var minutes))
            return TimeSpan.Zero;
        var offset = new TimeSpan(hours, minutes, 0);
        return value[0] == '-' ? -offset : offset;
    }

    /// <summary>
    /// The worktree <paramref name="path"/> is the root of, as git names it, or null when it's a plain
    /// directory or only sits inside one. Only git's own answer is comparable with the listing, which
    /// reports real paths — under macOS's <c>/var</c> symlink our spelling never matches.
    /// </summary>
    private async Task<string?> WorktreeRootAsync(string path, CancellationToken cancellationToken)
    {
        var run = await RunAsync(path, ["rev-parse", "--show-toplevel", "--show-prefix"], cancellationToken);
        if (run.Failure is not null || run.ExitCode != 0)
            return null;

        var lines = run.Output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        // A second, empty line is the prefix: anything else means path is a subdirectory of that worktree.
        return lines.Length > 1 && lines[0].Length > 0 && lines[1].Length == 0 ? lines[0] : null;
    }

    /// <summary>Reads <c>worktree list --porcelain</c>: blank-line separated records of <c>key value</c> lines.</summary>
    internal static IReadOnlyList<GitWorktree> ParseWorktrees(string output)
    {
        var worktrees = new List<GitWorktree>();
        string? path = null, branch = null, head = "";
        var skip = false;
        foreach (var line in output.Split('\n').Select(l => l.TrimEnd('\r')).Append(""))
        {
            if (line.Length == 0)
            {
                if (path is not null && !skip) worktrees.Add(new GitWorktree(path, branch, head));
                (path, branch, head, skip) = (null, null, "", false);
                continue;
            }

            var space = line.IndexOf(' ');
            var (key, value) = space < 0 ? (line, "") : (line[..space], line[(space + 1)..]);
            switch (key)
            {
                case "worktree": path = value; break;
                case "HEAD": head = value; break;
                case "branch": branch = value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value; break;
                case "bare" or "prunable": skip = true; break;
            }
        }
        return worktrees;
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
        var lines = run.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Some commands narrate on stderr before failing, so the fatal line beats the first one.
        if (lines.FirstOrDefault(l => l.StartsWith("fatal: ", StringComparison.Ordinal)) is { } fatal)
            return fatal["fatal: ".Length..];
        return lines.FirstOrDefault() ?? $"git exited with code {run.ExitCode}";
    }

    private Task<CliRun> RunAsync(
        string workingDirectory, IEnumerable<string> arguments, CancellationToken cancellationToken, TimeSpan? timeout = null, string? input = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = input is not null ? new UTF8Encoding(false) : null,
        };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(workingDirectory);
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        // English messages, so "not a git repository" can be recognised.
        info.Environment["LC_ALL"] = "C";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        return CliProcess.RunAsync("git", info, timeout ?? _timeout, cancellationToken, input);
    }
}
