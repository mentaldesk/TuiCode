using Microsoft.Extensions.Logging;

namespace TuiCode.Workbench.Files;

/// <summary>
/// Watches the files open in tabs for changes made outside the editor (#268), one non-recursive watcher
/// per distinct directory, torn down when its last file goes: on Linux each watcher is an inotify
/// instance out of a limit the whole machine shares, so watching the repo tree would be antisocial.
/// A watcher that can't be established, or that errors, is dropped and logged — nothing else changes.
/// </summary>
internal sealed class DiskWatcher : IDisposable
{
    // A truncate-then-rewrite arrives as several events over a file that's briefly half-written.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly IFileSystem _fileSystem;
    private readonly Action<TimeSpan, Action> _schedule;
    private readonly ILogger _logger;
    private readonly Dictionary<string, IFileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _followed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private bool _armed;
    private bool _disposed;

    /// <summary>The files something happened to, once the events have settled. Raised on the UI thread.</summary>
    public event EventHandler<IReadOnlyList<string>>? Changed;

    /// <param name="schedule">Runs the flush on the UI thread once the debounce window has passed.</param>
    public DiskWatcher(IFileSystem fileSystem, Action<TimeSpan, Action> schedule, ILogger logger)
    {
        _fileSystem = fileSystem;
        _schedule = schedule;
        _logger = logger;
    }

    internal int WatcherCount
    {
        get { lock (_gate) return _watchers.Count; }
    }

    /// <summary>Watch exactly <paramref name="paths"/>, adding and dropping directory watchers to match.</summary>
    public void Follow(IEnumerable<string> paths)
    {
        if (_disposed) return;
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        List<string> missing;
        lock (_gate)
        {
            _followed.Clear();
            foreach (var path in paths)
            {
                _followed.Add(path);
                if (_fileSystem.Path.GetDirectoryName(path) is { Length: > 0 } directory) wanted.Add(directory);
            }
            foreach (var gone in _watchers.Keys.Where(d => !wanted.Contains(d)).ToList()) Drop(gone);
            missing = wanted.Where(d => !_watchers.ContainsKey(d)).ToList();
        }
        foreach (var directory in missing) Add(directory);
    }

    private void Add(string directory)
    {
        IFileSystemWatcher? watcher = null;
        try
        {
            watcher = _fileSystem.FileSystemWatcher.New(directory);
            watcher.IncludeSubdirectories = false;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.Error += (_, e) => OnError(directory, e.GetException());
            watcher.EnableRaisingEvents = true;
            lock (_gate) _watchers[directory] = watcher;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or NotImplementedException)
        {
            // Out of inotify instances, or a directory we may not read: nothing to do but go without.
            // The save-time guard (#267) still stands whatever happens here.
            watcher?.Dispose();
            _logger.LogWarning(e, "Not watching {Directory} for changes made outside the editor", directory);
        }
    }

    private void OnError(string directory, Exception exception)
    {
        _logger.LogWarning(exception, "Stopped watching {Directory} for changes made outside the editor", directory);
        lock (_gate) Drop(directory);
    }

    // Under _gate.
    private void Drop(string directory)
    {
        if (_watchers.Remove(directory, out var watcher)) watcher.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;
        lock (_gate)
        {
            if (!_followed.Contains(e.FullPath)) return;
            _pending.Add(e.FullPath);
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
            changed = _pending.Where(_followed.Contains).ToArray();
            _pending.Clear();
        }
        if (!_disposed && changed.Length > 0) Changed?.Invoke(this, changed);
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
