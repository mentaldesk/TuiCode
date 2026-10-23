using System.Text;
using Terminal.Gui.Text;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Parts;

public sealed class StatusBarPart : View
{
    private readonly VerbatimLabel _label;
    private readonly VerbatimLabel _position;
    private readonly VerbatimLabel _help;
    private readonly VerbatimLabel _focus;
    private readonly Button _warnings;
    private int _unseenWarnings;
    private string _region = string.Empty;
    private string _message;
    private string? _chord;
    private string? _grammar;
    private string? _diff;
    private string? _wrap;
    private string? _mode;
    private (int Row, int Column)? _cursor;
    private (int Carets, int? Selected) _selection = (1, null);

    public StatusBarPart()
    {
        _message = DefaultMessage;
        Height = 1;
        CanFocus = false;
        SchemeName = "StatusBar";

        _help = new VerbatimLabel { X = Pos.AnchorEnd() - 1, Y = 0, Text = HelpHintFor("F1") };
        _position = new VerbatimLabel { Y = 0 };
        PlacePosition();
        _focus = new VerbatimLabel { X = 1, Y = 0 };
        _warnings = new Button
        {
            X = Pos.Left(_position),
            Y = 0,
            Visible = false,
            CanFocus = false,
            NoDecorations = true,
            NoPadding = true,
            ShadowStyle = ShadowStyles.None,
            HotKeySpecifier = (Rune)0xffff,
            MouseHighlightStates = MouseState.None,
        };
        _warnings.Accepting += (_, e) =>
        {
            e.Handled = true;
            WarningsClicked?.Invoke(this, EventArgs.Empty);
        };
        _warnings.GettingAttributeForRole += (_, e) =>
        {
            e.Result = WarningColour.On(e.Result ?? GetAttributeForRole(e.Role));
            e.Handled = true;
        };
        _label = new VerbatimLabel
        {
            X = Pos.Right(_focus),
            Y = 0,
            Width = Dim.Fill(2, _warnings),
            Text = _message
        };
        Add(_focus, _label, _warnings, _position, _help);
    }

    /// <summary>The <c>⚠ n</c> item was clicked.</summary>
    public event EventHandler? WarningsClicked;

    /// <summary>Warnings logged since the log was last opened, shown as <c>⚠ n</c> before the position; 0 hides it.</summary>
    public void SetWarnings(int unseen)
    {
        if (unseen == _unseenWarnings) return;
        _unseenWarnings = unseen;
        _warnings.Text = unseen > 0 ? $"⚠ {unseen}" : string.Empty;
        _warnings.Visible = unseen > 0;
        _warnings.X = Pos.Left(_position) - (unseen > 0 ? _warnings.Text.GetColumns(false) + 3 : 0);
    }

    internal string DisplayedWarnings => _warnings.Visible ? _warnings.Text : string.Empty;

    internal View WarningsView => _warnings;

    /// <summary>The one word for where keys will go, shown at the far left; always present (#227).</summary>
    public void SetFocusRegion(string region)
    {
        if (region == _region) return;
        _region = region;
        _focus.Text = $"{region}  •  ";
    }

    internal string DisplayedFocus => _region;

    public void SetMessage(string message)
    {
        _message = message;
        _label.SchemeName = null;
        UpdateLabel();
    }

    /// <summary>Show <paramref name="message"/> in the error colours until the next message replaces it.</summary>
    public void SetError(string message)
    {
        SetMessage(message);
        _label.SchemeName = "Error";
    }

    internal string Message => _message;

    /// <summary>What the bar says when nothing else has.</summary>
    public string DefaultMessage => "TuiCode";

    /// <summary>Ends the bar with <c>Press {key} for help</c>; null leaves nothing there.</summary>
    public void SetHelpKey(string? key)
    {
        _help.Text = HelpHintFor(key);
        PlacePosition();
    }

    private static string HelpHintFor(string? key) => key is null ? string.Empty : $"Press {key} for help";

    internal string DisplayedHelp => _help.Text;

    internal bool ShowsError => _label.SchemeName == "Error";

    /// <summary>Revert to the default message, unless something else has replaced <paramref name="message"/> since.</summary>
    public void ClearMessage(string message)
    {
        if (_message == message) SetMessage(DefaultMessage);
    }

    /// <summary>Where the focused diff tab is, shown after the message; null hides it.</summary>
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

    /// <summary>Whether the active tab wraps long lines, shown as <c>Wrap</c> after the grammar.</summary>
    public void SetWrap(bool wraps)
    {
        _wrap = wraps ? "Wrap" : null;
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

    private void ShowPosition()
    {
        if (_cursor is not var (row, column))
        {
            _position.Text = string.Empty;
            PlacePosition();
            return;
        }
        var (carets, selected) = _selection;
        var where = carets > 1 ? $"{carets} selections" : $"Ln {row + 1}, Col {column + 1}";
        _position.Text = selected is { } count ? $"{where} ({count:N0} selected)" : where;
        PlacePosition();
    }

    private void PlacePosition() =>
        _position.X = Pos.Left(_help) - _position.Text.GetColumns(false) - (_help.Text.Length > 0 && _position.Text.Length > 0 ? 3 : 0);

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
            : string.Join("  •  ", new[] { _message, _diff, _grammar, _mode, _wrap }.Where(part => part is not null));
}
