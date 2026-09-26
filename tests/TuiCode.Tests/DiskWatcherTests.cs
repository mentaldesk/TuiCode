using Microsoft.Extensions.Logging;
using TuiCode.Workbench.Files;

namespace TuiCode.Tests;

// One non-recursive watcher per open directory, events debounced and only for files we follow (#268).
public class DiskWatcherTests
{
    private const string A = "/work/a.txt";
    private const string B = "/work/b.txt";
    private const string C = "/work/sub/c.txt";

    private readonly WatchableFileSystem _fs = new();
    private readonly ListLogger<DiskWatcherTests> _logger = new();
    private readonly List<Action> _flushes = [];
    private readonly List<string> _reported = [];

    [Fact]
    public void A_change_to_a_file_we_follow_is_reported_once_the_events_settle()
    {
        using var watcher = Watch(A);

        _fs.Watchers.For("/work").RaiseChanged(A);
        Assert.Empty(_reported);

        Flush();
        Assert.Equal([A], _reported);
    }

    [Fact]
    public void Two_events_inside_the_debounce_window_produce_one_check()
    {
        using var watcher = Watch(A);

        _fs.Watchers.For("/work").RaiseChanged(A);
        _fs.Watchers.For("/work").RaiseChanged(A);

        Assert.Single(_flushes);
        Flush();
        Assert.Equal([A], _reported);
    }

    [Theory]
    [InlineData(WatcherChangeTypes.Created)]
    [InlineData(WatcherChangeTypes.Deleted)]
    [InlineData(WatcherChangeTypes.Renamed)]
    public void A_writer_that_replaces_the_file_rather_than_rewriting_it_is_reported_too(WatcherChangeTypes change)
    {
        using var watcher = Watch(A);

        _fs.Watchers.For("/work").Raise(change, A);
        Flush();

        Assert.Equal([A], _reported);
    }

    [Fact]
    public void A_change_to_a_file_nobody_has_open_is_ignored()
    {
        using var watcher = Watch(A);

        _fs.Watchers.For("/work").RaiseChanged(B);

        Assert.Empty(_flushes);
        Assert.Empty(_reported);
    }

    [Fact]
    public void A_file_closed_between_the_event_and_the_flush_is_not_reported()
    {
        using var watcher = Watch(A, B);

        _fs.Watchers.For("/work").RaiseChanged(A);
        watcher.Follow([B]);
        Flush();

        Assert.Empty(_reported);
    }

    [Fact]
    public void Twenty_files_across_several_directories_hold_one_watcher_each_directory()
    {
        var files = Enumerable.Range(0, 20).Select(i => $"/work/dir{i % 4}/f{i}.cs").ToList();
        using var watcher = Watch([.. files]);

        Assert.Equal(4, watcher.WatcherCount);
        Assert.Equal(4, _fs.Watchers.Live.Count());
        Assert.All(_fs.Watchers.Live, w => Assert.False(w.IncludeSubdirectories));
    }

    [Fact]
    public void Two_files_in_one_directory_share_a_watcher()
    {
        using var watcher = Watch(A, B);

        Assert.Equal(1, watcher.WatcherCount);
    }

    [Fact]
    public void Closing_the_last_file_in_a_directory_tears_its_watcher_down()
    {
        using var watcher = Watch(A, C);
        Assert.Equal(2, watcher.WatcherCount);

        watcher.Follow([A]);

        Assert.Equal(1, watcher.WatcherCount);
        Assert.True(_fs.Watchers.All.Single(w => w.Path == "/work/sub").Disposed);
    }

    [Fact]
    public void A_watcher_that_cannot_be_established_is_logged_and_left_out()
    {
        _fs.Watchers.FailFor.Add("/work");
        using var watcher = Watch(A, C);

        Assert.Equal(1, watcher.WatcherCount);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("/work"));

        _fs.Watchers.For("/work/sub").RaiseChanged(C);
        Flush();
        Assert.Equal([C], _reported);
    }

    [Fact]
    public void An_error_from_a_watcher_drops_that_directory_and_leaves_the_rest()
    {
        using var watcher = Watch(A, C);

        _fs.Watchers.For("/work").RaiseError(new InternalBufferOverflowException("too many changes"));

        Assert.Equal(1, watcher.WatcherCount);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("/work"));

        _fs.Watchers.For("/work/sub").RaiseChanged(C);
        Flush();
        Assert.Equal([C], _reported);
    }

    [Fact]
    public void Disposing_stops_watching_everything()
    {
        var watcher = Watch(A, C);

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
}
