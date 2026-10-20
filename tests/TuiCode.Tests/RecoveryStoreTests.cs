using TuiCode.Editor;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Copies of unsaved edits, one per file and process (#579).
public class RecoveryStoreTests
{
    private const string Directory = "/home/.tui/recovery";
    private const string File = "/work/a.txt";

    private static readonly RecoveryOwner Gone = new(101, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
    private static readonly RecoveryOwner Running = new(102, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
    private static readonly RecoveryOwner Me = new(103, new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc));

    private readonly MockFileSystem _fs = new();

    [Fact]
    public void Edits_kept_by_a_process_that_has_gone_are_found_as_they_were_kept()
    {
        var edits = Edits("mine\n");
        Store(Gone).Write(File, edits);

        Assert.Equal(edits, Store(Me).Find(File));
    }

    [Fact]
    public void Edits_kept_by_a_process_still_running_are_neither_found_nor_forgotten()
    {
        Store(Running).Write(File, Edits("theirs\n"));
        var store = Store(Me);

        store.Forget(File);

        Assert.Null(store.Find(File));
        Assert.Single(_fs.Directory.GetFiles(Directory));
    }

    [Fact]
    public void A_process_deletes_only_its_own_copy()
    {
        Store(Gone).Write(File, Edits("left\n"));
        var store = Store(Me);
        store.Write(File, Edits("mine\n"));

        store.Delete(File);

        Assert.Equal("left\n", store.Find(File)?.Text);
    }

    [Fact]
    public void Forgetting_drops_what_processes_that_have_gone_left()
    {
        Store(Gone).Write(File, Edits("left\n"));
        var store = Store(Me);

        store.Forget(File);

        Assert.Null(store.Find(File));
        Assert.Empty(_fs.Directory.GetFiles(Directory));
    }

    [Fact]
    public void The_newest_copy_wins()
    {
        Store(Gone).Write(File, Edits("older\n"));
        Thread.Sleep(5);
        Store(Gone with { ProcessId = 104 }).Write(File, Edits("newer\n"));

        Assert.Equal("newer\n", Store(Me).Find(File)?.Text);
    }

    [Fact]
    public void Another_files_copy_isnt_found()
    {
        Store(Gone).Write(File, Edits("mine\n"));

        Assert.Null(Store(Me).Find("/work/b.txt"));
    }

    [Fact]
    public void A_copy_is_written_whole_and_only_its_owner_can_read_it()
    {
        Store(Gone).Write(File, Edits("mine\n"));

        var path = Assert.Single(_fs.Directory.GetFiles(Directory));
        Assert.EndsWith("-101.json", path, StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, _fs.File.GetUnixFileMode(path));
    }

    [Fact]
    public void A_copy_that_doesnt_parse_is_skipped()
    {
        Store(Gone).Write(File, Edits("mine\n"));
        _fs.File.WriteAllText(Assert.Single(_fs.Directory.GetFiles(Directory)), "{ not json");

        Assert.Null(Store(Me).Find(File));
    }

    private RecoveryStore Store(RecoveryOwner self) => new(_fs, Directory, self, owner => owner == Running);

    private static RecoveredEdits Edits(string text) =>
        new(text, new ViewPosition(1, 2, 0, 0), new FileSnapshot(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 4, "one\n"));
}
