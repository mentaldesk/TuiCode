namespace TuiCode.Workbench.Parts;

public sealed class StatusBarPart : View
{
    private const string DefaultMessage = "TuiCode  •  F1 help  •  Ctrl+Q quit";
    private readonly Label _label;
    private readonly Label _position;
    private string _message = DefaultMessage;
    private string? _chord;
    private string? _hint;
    private string? _grammar;
    private string? _mode;
    private (int Row, int Column)? _cursor;
    private string? _idleHint;

    public StatusBarPart()
    {
        Height = 1;
        CanFocus = false;
        SchemeName = "StatusBar";

        _position = new Label { X = Pos.AnchorEnd() - 1, Y = 0 };
        _label = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(2, _position),
            Text = DefaultMessage
        };
        Add(_label, _position);
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

    /// <summary>An editor mode to flag after the grammar, e.g. column select; null hides it.</summary>
    public void SetMode(string? mode)
    {
        _mode = mode;
        UpdateLabel();
    }

    /// <summary>The active cursor's zero-based position, shown 1-based at the right; null hides it.</summary>
    public void SetPosition((int Row, int Column)? position)
    {
        if (position == _cursor) return;
        _cursor = position;
        ShowPosition();
    }

    /// <summary>Shown in the position's place while no file is open; null for nothing.</summary>
    public void SetIdleHint(string? hint)
    {
        _idleHint = hint;
        ShowPosition();
    }

    private void ShowPosition() =>
        _position.Text = _cursor is var (row, column) ? $"Ln {row + 1}, Col {column + 1}" : _idleHint ?? string.Empty;

    internal string DisplayedText => _label.Text;

    internal string DisplayedPosition => _position.Text;

    public void SetChord(string? chord)
    {
        _chord = chord;
        UpdateLabel();
    }

    private void UpdateLabel() =>
        _label.Text = _chord is not null
            ? $"{_chord}…"
            : string.Join("  •  ", new[] { _hint ?? _message, _grammar, _mode }.Where(part => part is not null));
}
