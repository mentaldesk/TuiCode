using Microsoft.Extensions.Logging;

namespace TuiCode.Workbench.Files;

/// <summary>
/// Watches the files open in tabs for changes made outside the editor (#268), one non-recursive watcher per
/// distinct directory on the way from each file up to the root, shared by every file underneath it and torn
/// down when the last of them goes. The ancestors are there because a watcher hears nothing about its own
/// directory being removed — on macOS an <c>rm -rf</c> reports only the directory, and only to a watcher
/// above it (#271) — so whichever directory in the chain goes, something is listening one level up.
/// On Linux each watcher is an inotify instance out of <c>fs.inotify.max_user_instances</c>, which the whole
/// login session shares, so the count is kept to the chains themselves: depth, not the tree's size. A watcher
/// that can't be established, or that errors, is dropped and logged — the files under it fall back to
/// checking on activation (#269).
/// </summary>
internal sealed class DiskWatcher : IDisposable
{
    // A truncate-then-rewrite arrives as several events over a file that's briefly half-written.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    // A directory holding open files reports what happens to them; one only on the chain up reports its
    // subdirectories and nothing else, so watching $HOME on the way past costs no file events.
    private const NotifyFilters FileEvents =
        NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName;
    private const NotifyFilters AncestorEvents = NotifyFilters.DirectoryName;

    private readonly IFileSystem _fileSystem;
    private readonly Action<TimeSpan, Action> _schedule;
    private readonly ILogger _logger;
    private readonly Dictionary<string, IFileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _followed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _filesUnder = new(StringComparer.Ordinal);
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

    /// <summary>
    /// Whether every directory above <paramref name="path"/> has a live watcher. One gap anywhere in the chain
    /// is a directory whose removal nothing would hear, so those tabs check on activation instead (#269) — as
    /// do the tabs under a directory that has itself gone.
    /// </summary>
    public bool IsWatching(string path)
    {
        if (_fileSystem.Path.GetDirectoryName(path) is not { Length: > 0 } directory) return false;
        lock (_gate)
        {
            foreach (var above in Chain(directory))
                if (!_watchers.ContainsKey(above)) return false;
        }
        return _fileSystem.Directory.Exists(directory);
    }

    /// <summary><paramref name="directory"/> and every directory above it, up to the root.</summary>
    private IEnumerable<string> Chain(string? directory)
    {
        for (; directory is { Length: > 0 }; directory = _fileSystem.Path.GetDirectoryName(directory))
            yield return directory;
    }

    /// <summary>Watch exactly <paramref name="paths"/>, adding and dropping directory watchers to match.</summary>
    public void Follow(IEnumerable<string> paths)
    {
        if (_disposed) return;
        var wanted = new Dictionary<string, NotifyFilters>(StringComparer.Ordinal);
        List<KeyValuePair<string, NotifyFilters>> missing;
        lock (_gate)
        {
            _followed.Clear();
            _filesUnder.Clear();
            foreach (var path in paths)
            {
                if (!_followed.Add(path)) continue;
                var events = FileEvents;
                foreach (var directory in Chain(_fileSystem.Path.GetDirectoryName(path)))
                {
                    wanted[directory] = wanted.GetValueOrDefault(directory) | events;
                    events = AncestorEvents;
                    if (!_filesUnder.TryGetValue(directory, out var files)) _filesUnder[directory] = files = [];
                    files.Add(path);
                }
            }
            foreach (var gone in _watchers.Keys.Where(d => !wanted.ContainsKey(d)).ToList()) Drop(gone);
            // A directory that was only on the way past last time may hold an open file now, or the reverse.
            foreach (var (directory, events) in wanted)
                if (_watchers.TryGetValue(directory, out var watcher) && watcher.NotifyFilter != events)
                    watcher.NotifyFilter = events;
            missing = wanted.Where(w => !_watchers.ContainsKey(w.Key)).ToList();
        }
        foreach (var (directory, events) in missing) Add(directory, events);
    }

    private void Add(string directory, NotifyFilters events)
    {
        IFileSystemWatcher? watcher = null;
        try
        {
            watcher = _fileSystem.FileSystemWatcher.New(directory);
            watcher.IncludeSubdirectories = false;
            watcher.NotifyFilter = events;
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnRenamed;
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

    private void OnFileEvent(object sender, FileSystemEventArgs e) => Note(e.ChangeType, e.FullPath);

    // A rename carries both ends: the file has appeared at FullPath and gone from OldFullPath, and a tab
    // could be open on either — the temp file an atomic save renames over ours, or ours renamed away (#271).
    private void OnRenamed(object sender, RenamedEventArgs e) => Note(e.ChangeType, e.FullPath, e.OldFullPath);

    private void Note(WatcherChangeTypes change, params ReadOnlySpan<string> paths)
    {
        if (_disposed) return;
        // A directory appearing or going takes its files with it; one merely touched says nothing new.
        var directoriesToo = change is not WatcherChangeTypes.Changed;
        lock (_gate)
        {
            var ours = false;
            foreach (var path in paths)
            {
                if (_followed.Contains(path))
                {
                    _pending.Add(path);
                    ours = true;
                }
                else if (directoriesToo && _filesUnder.TryGetValue(path, out var files))
                {
                    foreach (var file in files) _pending.Add(file);
                    ours = true;
                }
            }
            if (!ours || _armed) return;
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
            _filesUnder.Clear();
            _pending.Clear();
        }
    }
}
