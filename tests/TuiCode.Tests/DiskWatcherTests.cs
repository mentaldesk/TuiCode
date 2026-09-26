using Microsoft.Extensions.Logging;
using TuiCode.Workbench.Files;

namespace TuiCode.Tests;

// One non-recursive watcher per open directory, events debounced and only for files we follow (#268).
public class DiskWatcherTests
{
    private readonly WatchableFileSystem _fs = new();
    private readonly ListLogger<DiskWatcherTests> _logger = new();
    private readonly List<Action> _flushes = [];
    private readonly List<string> _reported = [];

    // Paths go through the file system rather than being written out: MockFileSystem rewrites a
    // POSIX path to a rooted Windows one, and the watcher keys off the directory it hands back.
    private readonly string _a;
    private readonly string _b;
    private readonly string _c;

    public DiskWatcherTests()
    {
        _a = Path("/work/a.txt");
        _b = Path("/work/b.txt");
        _c = Path("/work/sub/c.txt");
    }

    [Fact]
    public void A_change_to_a_file_we_follow_is_reported_once_the_events_settle()
    {
        using var watcher = Watch(_a);

        Watcher(_a).RaiseChanged(_a);
        Assert.Empty(_reported);

        Flush();
        Assert.Equal([_a], _reported);
    }

    [Fact]
    public void Two_events_inside_the_debounce_window_produce_one_check()
    {
        using var watcher = Watch(_a);

        Watcher(_a).RaiseChanged(_a);
        Watcher(_a).RaiseChanged(_a);

        Assert.Single(_flushes);
        Flush();
        Assert.Equal([_a], _reported);
    }

    [Theory]
    [InlineData(WatcherChangeTypes.Created)]
    [InlineData(WatcherChangeTypes.Deleted)]
    [InlineData(WatcherChangeTypes.Renamed)]
    public void A_writer_that_replaces_the_file_rather_than_rewriting_it_is_reported_too(WatcherChangeTypes change)
    {
        using var watcher = Watch(_a);

        Watcher(_a).Raise(change, _a);
        Flush();

        Assert.Equal([_a], _reported);
    }

    // A rename reports the new path, so a file moved out from under its tab is only recognisable by the old one.
    [Fact]
    public void A_file_renamed_away_from_a_path_we_follow_is_reported_against_that_path()
    {
        using var watcher = Watch(_a);

        Watcher(_a).RaiseRenamedAway(_a);
        Flush();

        Assert.Equal([_a], _reported);
    }

    [Fact]
    public void A_change_to_a_file_nobody_has_open_is_ignored()
    {
        using var watcher = Watch(_a);

        Watcher(_a).RaiseChanged(_b);

        Assert.Empty(_flushes);
        Assert.Empty(_reported);
    }

    [Fact]
    public void A_file_closed_between_the_event_and_the_flush_is_not_reported()
    {
        using var watcher = Watch(_a, _b);

        Watcher(_a).RaiseChanged(_a);
        watcher.Follow([_b]);
        Flush();

        Assert.Empty(_reported);
    }

    [Fact]
    public void Twenty_files_across_several_directories_hold_one_watcher_each_directory()
    {
        var files = Enumerable.Range(0, 20).Select(i => Path($"/work/dir{i % 4}/f{i}.cs")).ToList();
        using var watcher = Watch([.. files]);

        Assert.Equal(4, watcher.WatcherCount);
        Assert.Equal(4, _fs.Watchers.Live.Count());
        Assert.All(_fs.Watchers.Live, w => Assert.False(w.IncludeSubdirectories));
    }

    [Fact]
    public void Two_files_in_one_directory_share_a_watcher()
    {
        using var watcher = Watch(_a, _b);

        Assert.Equal(1, watcher.WatcherCount);
    }

    [Fact]
    public void Closing_the_last_file_in_a_directory_tears_its_watcher_down()
    {
        using var watcher = Watch(_a, _c);
        Assert.Equal(2, watcher.WatcherCount);

        watcher.Follow([_a]);

        Assert.Equal(1, watcher.WatcherCount);
        Assert.True(_fs.Watchers.All.Single(w => w.Path == Directory(_c)).Disposed);
    }

    [Fact]
    public void A_watcher_that_cannot_be_established_is_logged_and_left_out()
    {
        _fs.Watchers.FailFor.Add(Directory(_a));
        using var watcher = Watch(_a, _c);

        Assert.Equal(1, watcher.WatcherCount);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(Directory(_a)));

        Watcher(_c).RaiseChanged(_c);
        Flush();
        Assert.Equal([_c], _reported);
    }

    [Fact]
    public void An_error_from_a_watcher_drops_that_directory_and_leaves_the_rest()
    {
        using var watcher = Watch(_a, _c);

        Watcher(_a).RaiseError(new InternalBufferOverflowException("too many changes"));

        Assert.Equal(1, watcher.WatcherCount);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(Directory(_a)));

        Watcher(_c).RaiseChanged(_c);
        Flush();
        Assert.Equal([_c], _reported);
    }

    [Fact]
    public void Disposing_stops_watching_everything()
    {
        var watcher = Watch(_a, _c);

        watcher.Dispose();

        Assert.Equal(0, watcher.WatcherCount);
        Assert.All(_fs.Watchers.All, w => Assert.True(w.Disposed));
    }

    private DiskWatcher Watch(params string[] paths)
    {
        var watcher = new DiskWatcher(_fs, (_, flush) => _flushes.Add(flush), _logger);
        watcher.Changed += (_, changed) => _reported.AddRange(changed);
        watcher.Follow(paths);
        return watcher;
    }

    private void Flush()
    {
        foreach (var flush in _flushes.ToList()) flush();
        _flushes.Clear();
    }

    private string Path(string path) => _fs.Path.GetFullPath(path);

    private string Directory(string path) => _fs.Path.GetDirectoryName(path)!;

    private FakeFileSystemWatcher Watcher(string path) => _fs.Watchers.For(Directory(path));
}
