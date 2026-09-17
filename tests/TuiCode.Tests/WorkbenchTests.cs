using Terminal.Gui.ViewBase;
using TuiCode.Explorer;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

public class WorkbenchTests
{
    [Fact]
    public void Sidebar_is_visible_by_default()
    {
        using var workbench = Build();

        Assert.True(workbench.IsSidebarVisible);
        Assert.True(workbench.Sidebar.Visible);
    }

    [Fact]
    public void SetSidebarVisible_hides_sidebar_and_lets_editor_consume_the_freed_space()
    {
        using var workbench = Build();

        workbench.SetSidebarVisible(false);

        Assert.False(workbench.IsSidebarVisible);
        Assert.False(workbench.Sidebar.Visible);
        // Editor's X anchor moves off the sidebar's right edge to column 0.
        Assert.Equal(Pos.Absolute(0), workbench.Editor.X);
    }

    [Fact]
    public void ToggleSidebar_flips_visibility()
    {
        using var workbench = Build();

        workbench.ToggleSidebar();
        Assert.False(workbench.IsSidebarVisible);

        workbench.ToggleSidebar();
        Assert.True(workbench.IsSidebarVisible);
    }

    [Fact]
    public void OpenFile_opens_the_file_in_the_editor_and_makes_it_active()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("hello"));
        using var workbench = Build();
        var file = fs.FileInfo.New("/work/a.txt");

        workbench.OpenFile(file);

        Assert.Equal(file.FullName, workbench.Editor.Group.ActiveTab!.File.FullName);
    }

    [Fact]
    public void OpenFolder_closes_open_editors_and_re_roots_the_explorer()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/old/a.txt", new MockFileData("a"));
        fs.AddDirectory("/new");
        using var workbench = Build();
        var newDir = fs.DirectoryInfo.New("/new");
        workbench.Sidebar.Explorer.Open(fs.DirectoryInfo.New("/old"));
        workbench.OpenFile(fs.FileInfo.New("/old/a.txt"));
        Assert.NotEmpty(workbench.Editor.Group.Tabs);

        workbench.OpenFolder(newDir);

        Assert.Empty(workbench.Editor.Group.Tabs);
        Assert.Null(workbench.Editor.Group.ActiveTab);
        Assert.Equal(newDir.FullName, workbench.Sidebar.Explorer.Root!.FullName);
    }

    [Fact]
    public void OpenFolder_reopens_the_files_that_were_open_in_that_folder()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        fs.AddFile("/work/c.txt", new MockFileData("c"));
        var store = new WorkspaceStateStore(fs, "/state.json");
        using (var first = Build(store))
        {
            first.OpenFolder(fs.DirectoryInfo.New("/work"));
            first.OpenFile(fs.FileInfo.New("/work/a.txt"));
            first.OpenFile(fs.FileInfo.New("/work/b.txt"));
            first.OpenFile(fs.FileInfo.New("/work/c.txt"));
            first.OpenFile(fs.FileInfo.New("/work/b.txt"));
        }

        using var workbench = Build(store);
        workbench.OpenFolder(fs.DirectoryInfo.New("/work"));

        Assert.Equal(["/work/a.txt", "/work/b.txt", "/work/c.txt"], workbench.Editor.Group.Tabs.Select(t => t.File.FullName));
        Assert.Equal("/work/b.txt", workbench.Editor.Group.ActiveTab!.File.FullName);
    }

    [Fact]
    public void OpenFolder_skips_files_that_no_longer_exist()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        var store = new WorkspaceStateStore(fs, "/state.json");
        store.Save("/work", new WorkspaceState(["/work/gone.txt", "/work/a.txt"], "/work/gone.txt"));
        using var workbench = Build(store);

        workbench.OpenFolder(fs.DirectoryInfo.New("/work"));

        Assert.Equal(["/work/a.txt"], workbench.Editor.Group.Tabs.Select(t => t.File.FullName));
        Assert.Equal(["/work/a.txt"], store.Load("/work")!.Files);
    }

    [Fact]
    public void Closing_tabs_updates_the_saved_state()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        var store = new WorkspaceStateStore(fs, "/state.json");
        using var workbench = Build(store);
        workbench.OpenFolder(fs.DirectoryInfo.New("/work"));
        workbench.OpenFile(fs.FileInfo.New("/work/a.txt"));
        workbench.OpenFile(fs.FileInfo.New("/work/b.txt"));

        workbench.Editor.CloseActive();

        Assert.Equal(["/work/a.txt"], store.Load("/work")!.Files);
        Assert.Equal("/work/a.txt", store.Load("/work")!.ActiveFile);
    }

    [Fact]
    public void Switching_folders_keeps_each_folders_files()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/one/a.txt", new MockFileData("a"));
        fs.AddFile("/two/b.txt", new MockFileData("b"));
        var store = new WorkspaceStateStore(fs, "/state.json");
        using var workbench = Build(store);
        workbench.OpenFolder(fs.DirectoryInfo.New("/one"));
        workbench.OpenFile(fs.FileInfo.New("/one/a.txt"));

        workbench.OpenFolder(fs.DirectoryInfo.New("/two"));
        workbench.OpenFile(fs.FileInfo.New("/two/b.txt"));
        workbench.OpenFolder(fs.DirectoryInfo.New("/one"));

        Assert.Equal(["/one/a.txt"], workbench.Editor.Group.Tabs.Select(t => t.File.FullName));
        Assert.Equal(["/two/b.txt"], store.Load("/two")!.Files);
    }

    [Fact]
    public void Disposing_the_workbench_keeps_the_saved_files()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        var store = new WorkspaceStateStore(fs, "/state.json");
        var workbench = Build(store);
        workbench.OpenFolder(fs.DirectoryInfo.New("/work"));
        workbench.OpenFile(fs.FileInfo.New("/work/a.txt"));

        workbench.Dispose();

        Assert.Equal(["/work/a.txt"], store.Load("/work")!.Files);
    }

    private static Workbench.Workbench Build(WorkspaceStateStore? store = null) =>
        new(
            new SidebarPart(new FileExplorerView()),
            new EditorPart(),
            new StatusBarPart(),
            store);
}
