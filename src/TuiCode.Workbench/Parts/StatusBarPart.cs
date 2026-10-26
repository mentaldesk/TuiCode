using MentalDesk.Tui;
using MentalDesk.Tui.Chrome;

namespace TuiCode.Workbench.Parts;

/// <summary>The library's <see cref="AppStatusBar"/>, fed the active file's path, grammar, modes, diff status and position.</summary>
public sealed class StatusBarPart : View
{
    private string? _help;
    private string? _grammar;
    private string? _diff;
    private string? _wrap;
    private string? _mode;
    private (int Row, int Column)? _cursor;
    private (int Carets, int? Selected) _selection = (1, null);
    private string _position = string.Empty;

    public StatusBarPart()
    {
        Height = 1;
        CanFocus = false;
        SchemeName = "StatusBar";
        Bar = new AppStatusBar { Y = 0, RestingText = DefaultMessage };
        Add(Bar);
        SetHelpKey("F1");
    }

    public AppStatusBar Bar { get; }

    /// <summary>The <c>⚠ n</c> item was clicked.</summary>
    public event EventHandler? MessagesRequested
    {
        add => Bar.MessagesRequested += value;
        remove => Bar.MessagesRequested -= value;
    }

    internal string DisplayedWarnings => Bar.UnreadButton.Visible ? Bar.UnreadButton.Text : string.Empty;

    internal View WarningsView => Bar.UnreadButton;

    /// <summary>The one word for where keys will go, shown at the far left; always present (#227).</summary>
    public void SetFocusRegion(string region)
    {
        if (region != Bar.FocusWord) Bar.SetFocusWord(region);
    }

    internal string DisplayedFocus => Bar.FocusWord;

    /// <summary>A reply to what the user just did: it goes at their next key, or after five seconds.</summary>
    public StatusMessage ShowMessage(string message, Severity severity = Severity.Info) => Bar.ShowMessage(message, severity);

    /// <summary>Something that happened by itself: it stays five seconds, or for an error until a key after that.</summary>
    public StatusMessage ShowNews(string message, Severity severity = Severity.Info) => Bar.ShowNews(message, severity);

    public void Withdraw(StatusMessage? message)
    {
        if (message is not null) Bar.Withdraw(message);
    }

    /// <summary>What the bar shows while no message is: the active file's path, or <see cref="DefaultMessage"/>.</summary>
    public void SetResting(string? text) => Bar.RestingText = text ?? DefaultMessage;

    /// <summary>The message showing, or the resting text when none is.</summary>
    internal string Message => Bar.Message ?? Bar.RestingText;

    public string DefaultMessage => "TuiCode";

    /// <summary>Ends the bar with <c>Press {key} for help</c>; null leaves nothing there.</summary>
    public void SetHelpKey(string? key)
    {
        _help = key is null ? null : $"Press {key} for help";
        ShowState();
    }

    internal string DisplayedHelp => _help ?? string.Empty;

    internal bool ShowsError => Bar.MessageSeverity == Severity.Error;

    /// <summary>Where the focused diff tab is; null hides it.</summary>
    public void SetDiffStatus(string? status)
    {
        if (status == _diff) return;
        _diff = status;
        ShowState();
    }

    /// <summary>The active file's grammar; null hides it.</summary>
    public void SetGrammar(string? grammar)
    {
        if (grammar == _grammar) return;
        _grammar = grammar;
        ShowState();
    }

    /// <summary>Whether the active tab wraps long lines, shown as <c>Wrap</c> after the grammar.</summary>
    public void SetWrap(bool wraps)
    {
        var wrap = wraps ? "Wrap" : null;
        if (wrap == _wrap) return;
        _wrap = wrap;
        ShowState();
    }

    /// <summary>An editor mode to flag after the grammar, e.g. column select; null hides it.</summary>
    public void SetMode(string? mode)
    {
        if (mode == _mode) return;
        _mode = mode;
        ShowState();
    }

    /// <summary>The active cursor's zero-based position, shown 1-based; null hides it.</summary>
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
        var position = string.Empty;
        if (_cursor is var (row, column))
        {
            var (carets, selected) = _selection;
            var where = carets > 1 ? $"{carets} selections" : $"Ln {row + 1}, Col {column + 1}";
            position = selected is { } count ? $"{where} ({count:N0} selected)" : where;
        }
        if (position == _position) return;
        _position = position;
        ShowState();
    }

    internal string DisplayedPosition => _position;

    /// <summary>The chord, message or resting text at the left.</summary>
    internal string DisplayedText => Bar.Says;

    /// <summary>What sits together at the right end, before the help hint.</summary>
    internal string DisplayedState => string.Join(HintRow.Separator, StateParts());

    public void SetChord(string? chord) => Bar.SetChord(chord);

    private IEnumerable<string> StateParts() =>
        new[] { _grammar, _mode, _wrap, _diff, _position }.Where(part => !string.IsNullOrEmpty(part)).OfType<string>();

    private void ShowState() =>
        Bar.Hints.Show(StateParts().Append(_help).OfType<string>().Select(text => (text, (Action?)null)));
}
