namespace TuiCode.Workbench.Parts;

public sealed class SidebarPart : FrameView
{
    public SidebarPart()
    {
        Title = "Explorer";
        BorderStyle = LineStyle.Single;

        Add(new Label
        {
            X = 1,
            Y = 0,
            Text = "(file tree placeholder)"
        });
    }
}
