using TuiCode.Explorer;

namespace TuiCode.Workbench.Parts;

public sealed class SidebarPart : FrameView
{
    public FileExplorerView Explorer { get; }

    public SidebarPart(FileExplorerView explorer)
    {
        Explorer = explorer;
        Title = "Explorer";
        BorderStyle = LineStyle.Single;
        SchemeName = Services.SchemeNames.Sidebar;
        explorer.SchemeName = Services.SchemeNames.Sidebar;

        explorer.X = 0;
        explorer.Y = 0;
        explorer.Width = Dim.Fill();
        explorer.Height = Dim.Fill();

        Add(explorer);
    }
}
