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

    private static Workbench.Workbench Build() =>
        new(
            new SidebarPart(new FileExplorerView()),
            new EditorPart(),
            new StatusBarPart());
}
