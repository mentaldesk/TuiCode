namespace TuiCode.Workbench.Parts;

public sealed class StatusBarPart : View
{
    public StatusBarPart()
    {
        Height = 1;
        CanFocus = false;

        Add(new Label
        {
            X = 1,
            Y = 0,
            Text = "TuiCode  •  Ctrl+Q to quit"
        });
    }
}
