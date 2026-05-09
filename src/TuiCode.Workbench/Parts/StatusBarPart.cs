namespace TuiCode.Workbench.Parts;

public sealed class StatusBarPart : View
{
    private const string DefaultMessage = "TuiCode  •  Ctrl+Q to quit";
    private readonly Label _label;
    private string _message = DefaultMessage;
    private string? _chord;

    public StatusBarPart()
    {
        Height = 1;
        CanFocus = false;
        SchemeName = Services.SchemeNames.StatusBar;

        _label = new Label
        {
            X = 1,
            Y = 0,
            Text = DefaultMessage
        };
        Add(_label);
    }

    public void SetMessage(string message)
    {
        _message = message;
        UpdateLabel();
    }

    public void SetChord(string? chord)
    {
        _chord = chord;
        UpdateLabel();
    }

    private void UpdateLabel() =>
        _label.Text = _chord is null ? _message : $"{_chord}…";
}
