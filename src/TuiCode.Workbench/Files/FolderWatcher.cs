using Microsoft.Extensions.Logging;

namespace TuiCode.Workbench.Files;

/// <summary>
/// One non-recursive watcher per expanded Explorer folder, reporting folders whose entries appeared, went or were
/// renamed (#334). Past <see cref="Cap"/> only the most recently expanded folders are watched.
/// </summary>
internal sealed class FolderWatcher : IDisposable
{
    internal const int Cap = 64;

    // A git switch is a storm of events; one refresh per folder once it's over.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly IFileSystem _fileSystem;
    private readonly Action<TimeSpan, Action> _schedule;
    private readonly ILogger _logger;
    private readonly Dictionary<string, IFileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private bool _armed;
    private bool _disposed;

    /// <summary>The folders whose entries changed, once the events have settled. Raised on the UI thread.</summary>
    public event EventHandler<IReadOnlyList<string>>? Changed;

    /// <param name="schedule">Runs the flush on the UI thread once the debounce window has passed.</param>
    public FolderWatcher(IFileSystem fileSystem, Action<TimeSpan, Action> schedule, ILogger logger)
    {
        _fileSystem = fileSystem;
        _schedule = schedule;
        _logger = logger;
    }

    internal IReadOnlyCollection<string> Watched
    {
        get { lock (_gate) return _watchers.Keys.ToList(); }
    }

    /// <summary>Watch <paramref name="folders"/>, oldest expanded first, adding and dropping watchers to match.</summary>
    public void Follow(IReadOnlyList<string> folders)
    {
        if (_disposed) return;
        var wanted = folders.Skip(Math.Max(0, folders.Count - Cap)).ToHashSet(StringComparer.Ordinal);
        List<string> missing;
        lock (_gate)
        {
            foreach (var gone in _watchers.Keys.Where(f => !wanted.Contains(f)).ToList()) Drop(gone);
            missing = wanted.Where(f => !_watchers.ContainsKey(f)).ToList();
        }
        foreach (var folder in missing) Add(folder);
    }

    private void Add(string folder)
    {
        IFileSystemWatcher? watcher = null;
        try
        {
            watcher = _fileSystem.FileSystemWatcher.New(folder);
            watcher.IncludeSubdirectories = false;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;
            watcher.Created += (_, _) => Note(folder);
            watcher.Deleted += (_, _) => Note(folder);
            watcher.Renamed += (_, _) => Note(folder);
            watcher.Error += (_, e) => OnError(folder, e.GetException());
            watcher.EnableRaisingEvents = true;
            lock (_gate) _watchers[folder] = watcher;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or NotImplementedException)
        {
            watcher?.Dispose();
            _logger.LogWarning(e, "Not watching {Folder} for new, removed or renamed entries", folder);
        }
    }

    private void OnError(string folder, Exception exception)
    {
        _logger.LogWarning(exception, "Stopped watching {Folder} for new, removed or renamed entries", folder);
        lock (_gate) Drop(folder);
    }

    // Under _gate.
    private void Drop(string folder)
    {
        if (_watchers.Remove(folder, out var watcher)) watcher.Dispose();
    }

    private void Note(string folder)
    {
        if (_disposed) return;
        lock (_gate)
        {
            _pending.Add(folder);
            if (_armed) return;
            _armed = true;
        }
        _schedule(Debounce, Flush);
    }

    private void Flush()
    {
        string[] changed;
        lock (_gate)
        {
            _armed = false;
            changed = _pending.Where(_watchers.ContainsKey).ToArray();
            _pending.Clear();
        }
        if (!_disposed && changed.Length > 0) Changed?.Invoke(this, changed);
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_gate)
        {
            foreach (var folder in _watchers.Keys.ToList()) Drop(folder);
            _pending.Clear();
        }
    }
}
