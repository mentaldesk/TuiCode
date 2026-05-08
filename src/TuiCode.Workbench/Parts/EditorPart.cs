namespace TuiCode.Workbench.Parts;

public sealed class EditorPart : FrameView
{
    public EditorPart()
    {
        Title = "Editor";
        BorderStyle = LineStyle.Single;

        Add(new Label
        {
            X = 1,
            Y = 0,
            Text = "(open a file from the explorer to start editing)"
        });
    }
}
