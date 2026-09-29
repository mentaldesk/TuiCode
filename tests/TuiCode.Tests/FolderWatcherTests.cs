using Microsoft.Extensions.Logging;
using TuiCode.Workbench.Files;

namespace TuiCode.Tests;

// One non-recursive watcher per expanded folder, reporting entries that appear, go or are renamed (#334).
public class FolderWatcherTests
{
    private readonly WatchableFileSystem _fs = new();
    private readonly ListLogger<FolderWatcherTests> _logger = new();
    private readonly List<Action> _flushes = [];
    private readonly List<string> _reported = [];

    private readonly string _work;
    private readonly string _src;

    public FolderWatcherTests()
    {
        _work = Path("/work");
        _src = Path("/work/src");
    }

    [Fact]
    public void Each_expanded_folder_gets_one_non_recursive_watcher_for_its_entries()
    {
        using var watcher = Watch(_work, _src);

        Assert.Equal([_work, _src], _fs.Watchers.Live.Select(w => w.Path).Order());
        Assert.All(_fs.Watchers.Live, w => Assert.False(w.IncludeSubdirectories));
        Assert.All(_fs.Watchers.Live, w => Assert.Equal(NotifyFilters.FileName | NotifyFilters.DirectoryName, w.NotifyFilter));
    }

    [Theory]
    [InlineData(WatcherChangeTypes.Created)]
    [InlineData(WatcherChangeTypes.Deleted)]
    [InlineData(WatcherChangeTypes.Renamed)]
    public void An_entry_appearing_going_or_renamed_reports_its_folder_once_the_events_settle(WatcherChangeTypes change)
    {
        using var watcher = Watch(_work);

        _fs.Watchers.For(_work).Raise(change, Path("/work/a.txt"));
        Assert.Empty(_reported);

        Flush();
        Assert.Equal([_work], _reported);
    }

    [Fact]
    public void A_burst_of_events_in_one_folder_produces_one_refresh()
    {
        using var watcher = Watch(_work);

        for (var i = 0; i < 20; i++)
            _fs.Watchers.For(_work).Raise(WatcherChangeTypes.Created, Path($"/work/{i}.txt"));

        Assert.Single(_flushes);
        Flush();
        Assert.Equal([_work], _reported);
    }

    [Fact]
    public void Events_in_different_folders_refresh_each()
    {
        using var watcher = Watch(_work, _src);

        _fs.Watchers.For(_work).Raise(WatcherChangeTypes.Deleted, Path("/work/a.txt"));
        _fs.Watchers.For(_src).Raise(WatcherChangeTypes.Created, Path("/work/src/b.cs"));
        Flush();

        Assert.Equal([_work, _src], _reported.Order());
    }

    [Fact]
    public void Collapsing_a_folder_disposes_its_watcher()
    {
        using var watcher = Watch(_work, _src);
        var src = _fs.Watchers.For(_src);

        watcher.Follow([_work]);

        Assert.True(src.Disposed);
        Assert.Equal([_work], watcher.Watched);
    }

    [Fact]
    public void An_event_from_a_folder_collapsed_before_the_flush_is_dropped()
    {
        using var watcher = Watch(_work, _src);

        _fs.Watchers.For(_src).Raise(WatcherChangeTypes.Created, Path("/work/src/b.cs"));
        watcher.Follow([_work]);
        Flush();

        Assert.Empty(_reported);
    }

    [Fact]
    public void Past_the_cap_the_most_recently_expanded_folders_are_watched()
    {
        var folders = Enumerable.Range(0, FolderWatcher.Cap + 6).Select(i => Path($"/work/f{i:00}")).ToList();

        using var watcher = Watch([.. folders]);

        Assert.Equal(folders.Skip(6).Order(), watcher.Watched.Order());
        Assert.Equal(FolderWatcher.Cap, _fs.Watchers.Live.Count());
    }

    [Fact]
    public void A_watcher_that_fails_to_start_is_logged_and_skipped()
    {
        _fs.Watchers.FailFor.Add(_src);

        using var watcher = Watch(_work, _src);

        Assert.Equal([_work], watcher.Watched);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(_src));
    }

    [Fact]
    public void A_watcher_that_errors_is_logged_and_dropped()
    {
        using var watcher = Watch(_work, _src);
        var src = _fs.Watchers.For(_src);

        src.RaiseError(new IOException("gone"));

        Assert.True(src.Disposed);
        Assert.Equal([_work], watcher.Watched);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(_src));
    }

    [Fact]
    public void Disposing_stops_watching_everything()
    {
        var watcher = Watch(_work, _src);

        watcher.Dispose();

        Assert.Empty(watcher.Watched);
        Assert.All(_fs.Watchers.All, w => Assert.True(w.Disposed));
    }

    private FolderWatcher Watch(params string[] folders)
    {
        var watcher = new FolderWatcher(_fs, (_, flush) => _flushes.Add(flush), _logger);
        watcher.Changed += (_, changed) => _reported.AddRange(changed);
        watcher.Follow(folders);
        return watcher;
    }

    private void Flush()
    {
        foreach (var flush in _flushes.ToList()) flush();
        _flushes.Clear();
    }

    private string Path(string path) => _fs.Path.GetFullPath(path);
}
