namespace TuiCode.Workbench.Parts;

public sealed class StatusBarPart : View
{
    private const string DefaultMessage = "TuiCode  •  F1 help  •  Ctrl+Q quit";
    private readonly Label _label;
    private string _message = DefaultMessage;
    private string? _chord;
    private string? _hint;
    private string? _grammar;

    public StatusBarPart()
    {
        Height = 1;
        CanFocus = false;
        SchemeName = "StatusBar";

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

    /// <summary>
    /// Show a transient key hint (e.g. find's "Enter next match") in place of the message until cleared
    /// with null, at which point the message shows again. An in-flight chord still takes precedence.
    /// </summary>
    public void SetHint(string? hint)
    {
        _hint = hint;
        UpdateLabel();
    }

    /// <summary>The active file's grammar, shown after the message; null hides it.</summary>
    public void SetGrammar(string? grammar)
    {
        _grammar = grammar;
        UpdateLabel();
    }

    internal string DisplayedText => _label.Text;

    public void SetChord(string? chord)
    {
        _chord = chord;
        UpdateLabel();
    }

    private void UpdateLabel() =>
        _label.Text = _chord is not null ? $"{_chord}…"
            : _grammar is null ? _hint ?? _message
            : $"{_hint ?? _message}  •  {_grammar}";
}
