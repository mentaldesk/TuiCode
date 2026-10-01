using Microsoft.Extensions.Logging;

namespace TuiCode.Workbench.Git;

/// <summary>Notices <c>HEAD</c> moving in the open tabs' repos, whoever moved it (#371).</summary>
internal sealed class HeadWatcher : IDisposable
{
    // A rebase moves HEAD once per commit it replays.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private const NotifyFilters Events = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;

    private readonly IFileSystem _fileSystem;
    private readonly Action<TimeSpan, Action> _schedule;
    private readonly ILogger _logger;
    private readonly Dictionary<string, IFileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly HashSet<GitDirs> _followed = [];
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private bool _armed;
    private bool _disposed;

    /// <summary>The repos whose <c>HEAD</c> may have moved, once the events have settled. Raised on the UI thread.</summary>
    public event EventHandler<IReadOnlyList<GitDirs>>? Moved;

    /// <param name="schedule">Runs the flush on the UI thread once the debounce window has passed.</param>
    public HeadWatcher(IFileSystem fileSystem, Action<TimeSpan, Action> schedule, ILogger logger)
    {
        _fileSystem = fileSystem;
        _schedule = schedule;
        _logger = logger;
    }

    internal int WatcherCount
    {
        get { lock (_gate) return _watchers.Count; }
    }

    public bool IsWatching(GitDirs repo)
    {
        lock (_gate) return Directories(repo).All(d => _watchers.ContainsKey(d.Path));
    }

    /// <summary>Watch exactly <paramref name="repos"/>, adding and dropping watchers to match.</summary>
    public void Follow(IEnumerable<GitDirs> repos)
    {
        if (_disposed) return;
        List<(string Path, bool Recursive)> missing;
        lock (_gate)
        {
            _followed.Clear();
            _followed.UnionWith(repos);
            var wanted = _followed.SelectMany(Directories).Distinct().ToList();
            foreach (var gone in _watchers.Keys.Where(d => wanted.All(w => w.Path != d)).ToList()) Drop(gone);
            missing = wanted.Where(w => !_watchers.ContainsKey(w.Path)).ToList();
        }
        foreach (var (path, recursive) in missing) Add(path, recursive);
    }

    private IEnumerable<(string Path, bool Recursive)> Directories(GitDirs repo)
    {
        yield return (repo.GitDir, false);
        if (repo.CommonDir != repo.GitDir) yield return (repo.CommonDir, false);
        yield return (BranchesOf(repo), true);
    }

    private string BranchesOf(GitDirs repo) => _fileSystem.Path.Combine(repo.CommonDir, "refs", "heads");

    private void Add(string directory, bool recursive)
    {
        IFileSystemWatcher? watcher = null;
        try
        {
            watcher = _fileSystem.FileSystemWatcher.New(directory);
            watcher.IncludeSubdirectories = recursive;
            watcher.NotifyFilter = Events;
            watcher.Changed += (_, e) => OnEvent(e, recursive);
            watcher.Created += (_, e) => OnEvent(e, recursive);
            watcher.Deleted += (_, e) => OnEvent(e, recursive);
            watcher.Renamed += (_, e) => OnEvent(e, recursive);
            watcher.Error += (_, e) => OnError(directory, e.GetException());
            watcher.EnableRaisingEvents = true;
            lock (_gate) _watchers[directory] = watcher;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or NotImplementedException)
        {
            watcher?.Dispose();
            _logger.LogWarning(e, "Not watching {Directory} for commits and checkouts", directory);
        }
    }

    private void OnError(string directory, Exception exception)
    {
        _logger.LogWarning(exception, "Stopped watching {Directory} for commits and checkouts", directory);
        lock (_gate) Drop(directory);
    }

    // Under _gate.
    private void Drop(string directory)
    {
        if (_watchers.Remove(directory, out var watcher)) watcher.Dispose();
    }

    // Git writes a ref to <name>.lock and renames it into place, so the rename is what lands on the ref.
    private void OnEvent(FileSystemEventArgs e, bool branches)
    {
        if (_disposed) return;
        var name = _fileSystem.Path.GetFileName(e.FullPath);
        if (branches ? name.EndsWith(".lock", StringComparison.Ordinal) : name is not ("HEAD" or "packed-refs")) return;
        var path = _fileSystem.Path.GetFullPath(e.FullPath);
        lock (_gate)
        {
            _pending.Add(path);
            if (_armed) return;
            _armed = true;
        }
        _schedule(Debounce, Flush);
    }

    private void Flush()
    {
        string[] changed;
        GitDirs[] followed;
        lock (_gate)
        {
            _armed = false;
            changed = _pending.ToArray();
            _pending.Clear();
            followed = _followed.ToArray();
        }
        if (_disposed) return;
        var moved = followed.Where(repo => HeadPaths(repo).Any(p => changed.Contains(p, StringComparer.Ordinal))).ToList();
        if (moved.Count > 0) Moved?.Invoke(this, moved);
    }

    private IEnumerable<string> HeadPaths(GitDirs repo)
    {
        var head = _fileSystem.Path.Combine(repo.GitDir, "HEAD");
        yield return head;
        yield return _fileSystem.Path.Combine(repo.CommonDir, "packed-refs");
        if (Branch(head) is { } branch)
            yield return _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(BranchesOf(repo), branch));
    }

    // Null when HEAD is detached.
    private string? Branch(string head)
    {
        const string prefix = "ref: refs/heads/";
        try
        {
            var line = _fileSystem.File.ReadLines(head).FirstOrDefault();
            return line is not null && line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..].Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_gate)
        {
            foreach (var directory in _watchers.Keys.ToList()) Drop(directory);
            _followed.Clear();
            _pending.Clear();
        }
    }
}
