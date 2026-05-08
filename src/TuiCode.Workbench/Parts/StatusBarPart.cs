namespace TuiCode.Workbench.Parts;

public sealed class StatusBarPart : View
{
    private const string DefaultMessage = "TuiCode  •  Ctrl+Q to quit";
    private readonly Label _label;

    public StatusBarPart()
    {
        Height = 1;
        CanFocus = false;

        _label = new Label
        {
            X = 1,
            Y = 0,
            Text = DefaultMessage
        };
        Add(_label);
    }

    public void SetMessage(string message) => _label.Text = message;
}
