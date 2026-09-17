using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

public class WorkspaceStateStoreTests
{
    private const string StatePath = "/home/.tui/TuiCode.workspaces.json";

    [Fact]
    public void Load_returns_the_saved_state_for_the_folder()
    {
        var store = new WorkspaceStateStore(new MockFileSystem(), StatePath);
        store.Save("/a", new WorkspaceState(["/a/x.cs", "/a/y.cs"], "/a/y.cs"));
        store.Save("/b", new WorkspaceState(["/b/z.cs"], "/b/z.cs"));

        var state = store.Load("/a");

        Assert.NotNull(state);
        Assert.Equal(["/a/x.cs", "/a/y.cs"], state.Files);
        Assert.Equal("/a/y.cs", state.ActiveFile);
    }

    [Fact]
    public void Load_returns_null_for_a_folder_never_saved()
    {
        var store = new WorkspaceStateStore(new MockFileSystem(), StatePath);
        store.Save("/a", new WorkspaceState(["/a/x.cs"], null));

        Assert.Null(store.Load("/other"));
    }

    [Fact]
    public void Save_keeps_only_the_most_recently_used_folders()
    {
        var store = new WorkspaceStateStore(new MockFileSystem(), StatePath);
        for (var i = 0; i <= WorkspaceStateStore.MaxFolders; i++)
            store.Save($"/f{i}", new WorkspaceState([], null));

        Assert.Null(store.Load("/f0"));
        Assert.NotNull(store.Load("/f1"));
        Assert.NotNull(store.Load($"/f{WorkspaceStateStore.MaxFolders}"));
    }

    [Fact]
    public void Saving_a_folder_again_makes_it_most_recently_used()
    {
        var store = new WorkspaceStateStore(new MockFileSystem(), StatePath);
        store.Save("/f0", new WorkspaceState([], null));
        for (var i = 1; i < WorkspaceStateStore.MaxFolders; i++)
            store.Save($"/f{i}", new WorkspaceState([], null));

        store.Save("/f0", new WorkspaceState(["/f0/x.cs"], null));
        store.Save("/new", new WorkspaceState([], null));

        Assert.Equal(["/f0/x.cs"], store.Load("/f0")!.Files);
        Assert.Null(store.Load("/f1"));
    }

    [Fact]
    public void A_corrupt_file_loads_nothing_and_is_replaced_on_save()
    {
        var fs = new MockFileSystem();
        fs.AddFile(StatePath, new MockFileData("{ not json"));
        var store = new WorkspaceStateStore(fs, StatePath);

        Assert.Null(store.Load("/a"));

        store.Save("/a", new WorkspaceState(["/a/x.cs"], "/a/x.cs"));
        Assert.Equal(["/a/x.cs"], store.Load("/a")!.Files);
    }

    [Fact]
    public void Entries_with_wrong_types_are_ignored()
    {
        var fs = new MockFileSystem();
        fs.AddFile(StatePath, new MockFileData("""
            [
              { "Folder": 1, "Files": [] },
              { "Folder": "/a", "Files": [2], "Active": "/a/x.cs" },
              { "Folder": "/b", "Files": ["/b/x.cs"] }
            ]
            """));
        var store = new WorkspaceStateStore(fs, StatePath);

        Assert.Null(store.Load("/a"));
        Assert.Equal(["/b/x.cs"], store.Load("/b")!.Files);
    }
}
