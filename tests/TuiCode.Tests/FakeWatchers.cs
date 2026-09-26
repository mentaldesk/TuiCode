using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Abstractions;

namespace TuiCode.Tests;

/// <summary>
/// MockFileSystem ships no FileSystemWatcher, so the watching rules (#268) are exercised by raising
/// the events by hand. <see cref="FakeWatcherFactory.FailFor"/> makes creation throw the way a machine
/// out of inotify instances does.
/// </summary>
internal sealed class WatchableFileSystem : MockFileSystem
{
    public FakeWatcherFactory Watchers { get; }

    public WatchableFileSystem() => Watchers = new FakeWatcherFactory(this);

    public override IFileSystemWatcherFactory FileSystemWatcher => Watchers;
}

internal sealed class FakeWatcherFactory(IFileSystem fileSystem) : IFileSystemWatcherFactory
{
    public IFileSystem FileSystem { get; } = fileSystem;

    public List<FakeFileSystemWatcher> All { get; } = [];

    /// <summary>Directories whose watcher can't be established.</summary>
    public HashSet<string> FailFor { get; } = new(StringComparer.Ordinal);

    public IEnumerable<FakeFileSystemWatcher> Live => All.Where(w => w is { Disposed: false, EnableRaisingEvents: true });

    public FakeFileSystemWatcher For(string directory) =>
        Live.Single(w => string.Equals(w.Path, directory, StringComparison.Ordinal));

    public IFileSystemWatcher New() => throw new NotSupportedException();

    public IFileSystemWatcher New(string path)
    {
        if (FailFor.Contains(path)) throw new IOException("The configured user limit on the number of inotify instances has been reached.");
        var watcher = new FakeFileSystemWatcher(FileSystem, path);
        All.Add(watcher);
        return watcher;
    }

    public IFileSystemWatcher New(string path, string filter) => New(path);

    public IFileSystemWatcher? Wrap(System.IO.FileSystemWatcher? watcher) => throw new NotSupportedException();
}

internal sealed class FakeFileSystemWatcher(IFileSystem fileSystem, string path) : IFileSystemWatcher
{
    public IFileSystem FileSystem { get; } = fileSystem;
    public bool Disposed { get; private set; }

    public void RaiseChanged(string fullPath) => Raise(WatcherChangeTypes.Changed, fullPath);

    /// <summary>What an editor that writes a temp file and renames it over the original looks like.</summary>
    public void Raise(WatcherChangeTypes change, string fullPath)
    {
        var name = FileSystem.Path.GetFileName(fullPath);
        var args = new FileSystemEventArgs(change, Path, name);
        switch (change)
        {
            case WatcherChangeTypes.Created: Created?.Invoke(this, args); break;
            case WatcherChangeTypes.Deleted: Deleted?.Invoke(this, args); break;
            case WatcherChangeTypes.Renamed: Renamed?.Invoke(this, new RenamedEventArgs(change, Path, name, name + ".tmp")); break;
            default: Changed?.Invoke(this, args); break;
        }
    }

    public void RaiseError(Exception exception) => Error?.Invoke(this, new ErrorEventArgs(exception));

    public event FileSystemEventHandler? Changed;
    public event FileSystemEventHandler? Created;
    public event FileSystemEventHandler? Deleted;
    public event RenamedEventHandler? Renamed;
    public event ErrorEventHandler? Error;

    public bool EnableRaisingEvents { get; set; }
    public string Filter { get; set; } = "*";
    public Collection<string> Filters { get; } = [];
    public bool IncludeSubdirectories { get; set; }
    public int InternalBufferSize { get; set; } = 8192;
    public NotifyFilters NotifyFilter { get; set; }
    public string Path { get; set; } = path;
    public IContainer? Container => null;
    public ISite? Site { get; set; }
    public ISynchronizeInvoke? SynchronizingObject { get; set; }

    public void BeginInit() { }
    public void EndInit() { }

    public IWaitForChangedResult WaitForChanged(WatcherChangeTypes changeType) => throw new NotSupportedException();
    public IWaitForChangedResult WaitForChanged(WatcherChangeTypes changeType, int timeout) => throw new NotSupportedException();
    public IWaitForChangedResult WaitForChanged(WatcherChangeTypes changeType, TimeSpan timeout) => throw new NotSupportedException();

    public void Dispose()
    {
        Disposed = true;
        EnableRaisingEvents = false;
    }
}
