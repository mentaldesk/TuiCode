using Terminal.Gui.ViewBase;
using TuiCode.Explorer;
using TuiCode.Workbench.Parts;

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

    private static Workbench.Workbench Build() =>
        new(
            new SidebarPart(new FileExplorerView()),
            new EditorPart(),
            new StatusBarPart());
}
