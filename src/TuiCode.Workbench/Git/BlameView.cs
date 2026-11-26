using MentalDesk.Tui;
using ButtonKind = MentalDesk.Tui.Dialogs.ButtonKind;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Git;

/// <summary>The <c>gb</c> dialog (#330): who last changed the cursor's line, when, and in which commit.</summary>
public sealed class BlameView : Window
{
    internal const string NotCommitted = "Not committed yet — this line isn't in any commit.";
    private const int DialogWidth = 70;

    private readonly AlertView _alert;
    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    /// <summary>Esc or Close: the host closes the dialog and gives the keys back.</summary>
    public event EventHandler? Closed;

    /// <summary>Enter or View change on a committed line: the host opens the change that introduced it (#331).</summary>
    public event EventHandler? OpenChange;

    /// <summary>Focus moved elsewhere, a click in the editor say: the host closes the dialog and leaves the keys there.</summary>
    public event EventHandler? FocusLeft;

    public BlameView(string file, int lineNumber, GitBlameLine blame, Times times)
    {
        Title = "Blame";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        CanFocus = true;

        Location = $"{file}:{lineNumber}";
        LineText = $"  {lineNumber} │ {blame.Text.Replace("\t", "    ", StringComparison.Ordinal)}";
        if (blame.IsCommitted)
        {
            Commit = $"{blame.ShortHash}  {blame.Author}  {times.Ago(blame.Date)}";
            Subject = blame.Subject;
        }

        var y = 0;
        Add(Row(Location, ref y));
        y++;
        if (Commit is not null)
        {
            Add(Row(Commit, ref y));
            Add(Row(Subject!, ref y));
            y++;
        }
        Add(Row(LineText, ref y));
        y++;

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.BlameClose, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.BlameClose);
        // Bound either way, so Enter on an uncommitted line doesn't press the focused Close.
        _scopeCommands.Register(CommandIds.BlameOpenChange, () =>
        {
            if (blame.IsCommitted) OpenChange?.Invoke(this, EventArgs.Empty);
        });
        _scopeKeybindings.Bind("Enter", CommandIds.BlameOpenChange);

        if (blame.IsCommitted)
        {
            Add(Foot(KeyedButton.Of(ButtonKind.Primary, _scopeKeybindings, CommandIds.BlameOpenChange, "View change"), y, () => OpenChange?.Invoke(this, EventArgs.Empty)));
        }
        var close = Foot(KeyedButton.Of(blame.IsCommitted ? ButtonKind.Secondary : ButtonKind.Primary, _scopeKeybindings, CommandIds.BlameClose, "Close"), y, () => Closed?.Invoke(this, EventArgs.Empty));

        _alert = new AlertView(DialogWidth - 2) { X = 0, Y = y + 1 };
        if (!blame.IsCommitted) _alert.Show(NotCommitted, AlertSeverity.Info);
        Add(close, _alert);
        Height = y + 1 + _alert.Lines + 2;
    }

    internal string Location { get; }

    /// <summary>Hash, author and date; null for a line no commit has.</summary>
    internal string? Commit { get; }

    internal string? Subject { get; }

    internal string LineText { get; }

    internal string Status => _alert.Message;

    private static Button Foot(Button button, int y, Action accepted)
    {
        button.X = Pos.Align(Alignment.Center);
        button.Y = y;
        button.Accepting += (_, e) => { e.Handled = true; accepted(); };
        return button;
    }

    private static VerbatimLabel Row(string text, ref int y) => new() { X = 1, Y = y++, Width = Dim.Fill(1), Height = 1, Text = text };

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView)
    {
        base.OnHasFocusChanged(newHasFocus, previousFocusedView, focusedView);
        if (!newHasFocus) FocusLeft?.Invoke(this, EventArgs.Empty);
    }
}
