using TuiCode.Editor;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

public class FilePositionStoreTests
{
    private const string StorePath = "/home/.tui/TuiCode.positions.json";

    [Fact]
    public void Load_returns_what_was_saved_for_the_file()
    {
        var store = new FilePositionStore(new MockFileSystem(), StorePath);
        store.Save([("/a/x.cs", new ViewPosition(40, 7, 30, 2)), ("/b/y.cs", new ViewPosition(1, 0, 0, 0))]);

        Assert.Equal(new ViewPosition(40, 7, 30, 2), store.Load("/a/x.cs"));
        Assert.Equal(new ViewPosition(1, 0, 0, 0), store.Load("/b/y.cs"));
    }

    [Fact]
    public void Load_returns_null_for_a_file_never_saved_or_with_no_store_file()
    {
        var store = new FilePositionStore(new MockFileSystem(), StorePath);
        Assert.Null(store.Load("/a/x.cs"));

        store.Save([("/a/x.cs", new ViewPosition(1, 1, 0, 0))]);

        Assert.Null(store.Load("/a/other.cs"));
    }

    [Fact]
    public void Saving_a_file_again_replaces_its_position()
    {
        var store = new FilePositionStore(new MockFileSystem(), StorePath);
        store.Save([("/a/x.cs", new ViewPosition(1, 1, 0, 0))]);

        store.Save([("/a/x.cs", new ViewPosition(9, 3, 5, 0))]);

        Assert.Equal(new ViewPosition(9, 3, 5, 0), store.Load("/a/x.cs"));
    }

    [Fact]
    public void Save_keeps_only_the_most_recently_used_files()
    {
        var store = new FilePositionStore(new MockFileSystem(), StorePath);
        for (var i = 0; i <= FilePositionStore.MaxFiles; i++)
            store.Save([($"/f{i}.cs", new ViewPosition(i, 0, 0, 0))]);

        Assert.Null(store.Load("/f0.cs"));
        Assert.NotNull(store.Load("/f1.cs"));
        Assert.NotNull(store.Load($"/f{FilePositionStore.MaxFiles}.cs"));
    }

    [Fact]
    public void Saving_a_file_again_makes_it_most_recently_used()
    {
        var store = new FilePositionStore(new MockFileSystem(), StorePath);
        store.Save([("/f0.cs", new ViewPosition(0, 0, 0, 0))]);
        for (var i = 1; i < FilePositionStore.MaxFiles; i++)
            store.Save([($"/f{i}.cs", new ViewPosition(0, 0, 0, 0))]);

        store.Save([("/f0.cs", new ViewPosition(5, 0, 0, 0))]);
        store.Save([("/new.cs", new ViewPosition(0, 0, 0, 0))]);

        Assert.Equal(5, store.Load("/f0.cs")!.Line);
        Assert.Null(store.Load("/f1.cs"));
    }

    [Fact]
    public void Files_inside_a_git_folder_are_never_recorded()
    {
        var fs = new MockFileSystem();
        var store = new FilePositionStore(fs, StorePath);

        store.Save([("/repo/.git/COMMIT_EDITMSG", new ViewPosition(3, 0, 0, 0)), ("/repo/.git/worktrees/a/COMMIT_EDITMSG", new ViewPosition(3, 0, 0, 0))]);

        Assert.False(fs.File.Exists(StorePath));
        Assert.Null(store.Load("/repo/.git/COMMIT_EDITMSG"));
    }

    [Fact]
    public void A_file_saved_inside_a_git_folder_by_hand_is_still_not_restored()
    {
        var fs = new MockFileSystem();
        fs.AddFile(StorePath, new MockFileData("""[{ "File": "/repo/.git/COMMIT_EDITMSG", "Line": 3, "Column": 0, "Top": 0, "Left": 0 }]"""));

        Assert.Null(new FilePositionStore(fs, StorePath).Load("/repo/.git/COMMIT_EDITMSG"));
    }

    [Fact]
    public void A_corrupt_file_loads_nothing_and_is_replaced_on_save()
    {
        var fs = new MockFileSystem();
        fs.AddFile(StorePath, new MockFileData("{ not json"));
        var store = new FilePositionStore(fs, StorePath);

        Assert.Null(store.Load("/a/x.cs"));

        store.Save([("/a/x.cs", new ViewPosition(2, 0, 0, 0))]);
        Assert.Equal(2, store.Load("/a/x.cs")!.Line);
    }

    [Fact]
    public void Entries_with_wrong_types_are_ignored()
    {
        var fs = new MockFileSystem();
        fs.AddFile(StorePath, new MockFileData("""
            [
              { "File": 1, "Line": 2 },
              { "File": "/a/x.cs", "Line": "two" },
              { "File": "/b/y.cs", "Line": 4, "Column": 1 }
            ]
            """));
        var store = new FilePositionStore(fs, StorePath);

        Assert.Null(store.Load("/a/x.cs"));
        Assert.Equal(new ViewPosition(4, 1, 0, 0), store.Load("/b/y.cs"));
    }

    [Fact]
    public void A_file_that_cannot_be_read_logs_a_warning_naming_it_once()
    {
        var fs = new MockFileSystem();
        fs.AddFile(StorePath, new MockFileData("{ not json"));
        var logger = new ListLogger<FilePositionStore>();
        var store = new FilePositionStore(fs, StorePath, logger);

        store.Load("/a/x.cs");
        store.Load("/b/y.cs");

        Assert.Equal($"Couldn't read where you were in each file from {StorePath}", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public void A_file_that_cannot_be_written_logs_a_warning_naming_it_until_a_save_succeeds()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/home/.tui", new MockFileData("a file where the folder should be"));
        var logger = new ListLogger<FilePositionStore>();
        var store = new FilePositionStore(fs, StorePath, logger);

        store.Save([("/a/x.cs", new ViewPosition(1, 0, 0, 0))]);
        store.Save([("/a/x.cs", new ViewPosition(2, 0, 0, 0))]);

        Assert.Equal($"Couldn't save where you were in each file to {StorePath}", Assert.Single(logger.Entries).Message);
    }
}
