namespace TuiCode.Workbench.Parts;

public sealed class StatusBarPart : View
{
    internal const string DefaultMessage = "TuiCode  •  F1 help  •  Ctrl+Q quit";
    private readonly Label _label;
    private readonly Label _position;
    private string _message = DefaultMessage;
    private string? _chord;
    private string? _hint;
    private string? _grammar;
    private string? _diff;
    private string? _mode;
    private (int Row, int Column)? _cursor;
    private (int Carets, int? Selected) _selection = (1, null);
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

    /// <summary>Revert to the default message, unless something else has replaced <paramref name="message"/> since.</summary>
    public void ClearMessage(string message)
    {
        if (_message == message) SetMessage(DefaultMessage);
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

    /// <summary>Where the focused diff tab is and its keys, shown after the message; null hides it.</summary>
    public void SetDiffStatus(string? status)
    {
        if (status == _diff) return;
        _diff = status;
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

    /// <summary>How many carets there are and the characters they select (null for none), shown with the position.</summary>
    public void SetSelection(int carets, int? selected)
    {
        if ((carets, selected) == _selection) return;
        _selection = (carets, selected);
        ShowPosition();
    }

    /// <summary>Shown in the position's place while no file is open; null for nothing.</summary>
    public void SetIdleHint(string? hint)
    {
        _idleHint = hint;
        ShowPosition();
    }

    private void ShowPosition()
    {
        if (_cursor is not var (row, column))
        {
            _position.Text = _idleHint ?? string.Empty;
            return;
        }
        var (carets, selected) = _selection;
        var where = carets > 1 ? $"{carets} selections" : $"Ln {row + 1}, Col {column + 1}";
        _position.Text = selected is { } count ? $"{where} ({count:N0} selected)" : where;
    }

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
            : string.Join("  •  ", new[] { _hint ?? _message, _diff, _grammar, _mode }.Where(part => part is not null));
}
