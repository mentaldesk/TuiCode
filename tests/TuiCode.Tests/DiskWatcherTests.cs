using Microsoft.Extensions.Logging;
using TuiCode.Workbench.Files;

namespace TuiCode.Tests;

// One non-recursive watcher per directory on the way from an open file up to the root, events debounced
// and only for files we follow (#268).
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

    // What it costs is the chains up from the open files, not the size of the tree they're in.
    [Fact]
    public void Twenty_files_across_several_directories_hold_one_watcher_per_directory_on_the_way_up()
    {
        var files = Enumerable.Range(0, 20).Select(i => Path($"/work/dir{i % 4}/f{i}.cs")).ToList();
        using var watcher = Watch([.. files]);

        // Four directories of their own, then /work and the root they share.
        Assert.Equal(6, watcher.WatcherCount);
        Assert.Equal(6, _fs.Watchers.Live.Count());
        Assert.All(_fs.Watchers.Live, w => Assert.False(w.IncludeSubdirectories));
        // Without this a directory removed whole is silent on macOS (#271).
        Assert.All(_fs.Watchers.Live, w => Assert.True(w.NotifyFilter.HasFlag(NotifyFilters.DirectoryName)));
    }

    [Fact]
    public void Files_in_different_trees_share_every_watcher_their_paths_have_in_common()
    {
        using var watcher = Watch(Path("/work/one/a.cs"), Path("/work/two/b.cs"));

        // /work/one and /work/two, then /work and the root.
        Assert.Equal(4, watcher.WatcherCount);
    }

    // A directory only on the way up is asked about its subdirectories and nothing else: watching $HOME on
    // the way past shouldn't wake us for every dotfile written in it.
    [Fact]
    public void A_directory_holding_no_open_file_asks_about_subdirectories_only()
    {
        using var watcher = Watch(_c);

        Assert.Equal(NotifyFilters.DirectoryName, ParentWatcher(_c).NotifyFilter);
        Assert.True(Watcher(_c).NotifyFilter.HasFlag(NotifyFilters.FileName));
    }

    [Fact]
    public void A_directory_that_gains_an_open_file_starts_asking_about_files()
    {
        using var watcher = Watch(_c);

        watcher.Follow([_a, _c]);

        Assert.True(Watcher(_a).NotifyFilter.HasFlag(NotifyFilters.FileName));
    }

    [Fact]
    public void Two_files_in_one_directory_share_a_watcher()
    {
        using var watcher = Watch(_a, _b);

        Assert.Equal(1, _fs.Watchers.Live.Count(w => string.Equals(w.Path, Directory(_a), StringComparison.Ordinal)));
    }

    // rm -rf of a directory a tab's file sits under: macOS reports the directory and nothing else, so the
    // watcher on the directory itself never hears a thing (#271).
    [Theory]
    [InlineData(WatcherChangeTypes.Deleted)]
    [InlineData(WatcherChangeTypes.Created)]
    public void A_directory_that_goes_or_comes_back_reports_every_file_we_follow_beneath_it(WatcherChangeTypes change)
    {
        using var watcher = Watch(_a, _b, _c);

        ParentWatcher(_a).Raise(change, Directory(_a));
        Flush();

        Assert.Equal([_a, _b, _c], _reported.Order(StringComparer.Ordinal).ToArray());
    }

    // Which is the point of going all the way up rather than stopping at the file's own parent.
    [Fact]
    public void A_directory_several_levels_above_a_file_reports_it_when_it_goes()
    {
        var deep = Path("/work/one/two/three/deep.cs");
        using var watcher = Watch(deep);

        _fs.Watchers.For(Path("/work")).Raise(WatcherChangeTypes.Deleted, Path("/work/one"));
        Flush();

        Assert.Equal([deep], _reported);
    }

    [Fact]
    public void A_directory_renamed_away_reports_every_file_we_follow_inside_it()
    {
        using var watcher = Watch(_a, _b);

        ParentWatcher(_a).RaiseRenamedAway(Directory(_a));
        Flush();

        Assert.Equal([_a, _b], _reported.Order(StringComparer.Ordinal).ToArray());
    }

    // A directory's mtime bumps whenever anything inside it is written; its own watcher has already said so.
    [Fact]
    public void A_directory_merely_touched_reports_nothing()
    {
        using var watcher = Watch(_a, _b);

        ParentWatcher(_a).RaiseChanged(Directory(_a));

        Assert.Empty(_flushes);
        Assert.Empty(_reported);
    }

    [Fact]
    public void A_directory_nobody_has_a_file_open_in_is_ignored()
    {
        using var watcher = Watch(_a);

        ParentWatcher(_c).Raise(WatcherChangeTypes.Deleted, Directory(_c));

        Assert.Empty(_flushes);
        Assert.Empty(_reported);
    }

    [Fact]
    public void Closing_the_last_file_in_a_directory_tears_its_watcher_down()
    {
        using var watcher = Watch(_a, _c);
        Assert.Equal(3, watcher.WatcherCount);

        watcher.Follow([_a]);

        Assert.Equal(2, watcher.WatcherCount);
        Assert.True(_fs.Watchers.All.Single(w => w.Path == Directory(_c)).Disposed);
    }

    // A gap anywhere in the chain is a directory whose removal nothing would hear, so those tabs go back to
    // checking on activation (#269) rather than trusting a watcher that can't cover them.
    [Fact]
    public void A_file_under_a_directory_we_could_not_watch_counts_as_unwatched()
    {
        _fs.AddDirectory(Directory(_c));
        _fs.Watchers.FailFor.Add(Directory(_a));
        using var watcher = Watch(_c);

        Assert.False(watcher.IsWatching(_c));
    }

    [Fact]
    public void A_file_whose_every_directory_is_watched_counts_as_watched()
    {
        _fs.AddDirectory(Directory(_c));
        using var watcher = Watch(_c);

        Assert.True(watcher.IsWatching(_c));
    }

    [Fact]
    public void A_watcher_that_cannot_be_established_is_logged_and_left_out()
    {
        _fs.Watchers.FailFor.Add(Directory(_a));
        using var watcher = Watch(_a, _c);

        Assert.Equal(2, watcher.WatcherCount);
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

        Assert.Equal(2, watcher.WatcherCount);
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

    private FakeFileSystemWatcher ParentWatcher(string path) => _fs.Watchers.For(Directory(Directory(path)));
}
