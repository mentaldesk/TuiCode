using System.Globalization;
using System.Text;
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

    /// <summary>Esc or the hint: the host closes the dialog and gives the keys back.</summary>
    public event EventHandler? Closed;

    /// <summary>Enter or its hint on a committed line: the host opens the change that introduced it (#331).</summary>
    public event EventHandler? OpenChange;

    /// <summary>Focus moved elsewhere, a click in the editor say: the host closes the dialog and leaves the keys there.</summary>
    public event EventHandler? FocusLeft;

    public BlameView(string file, int lineNumber, GitBlameLine blame, DateTimeOffset now)
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
            Commit = $"{blame.ShortHash}  {blame.Author}  {When(blame.Date, now)}";
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

        if (blame.IsCommitted)
        {
            var open = Hint("Enter view change", y);
            open.Accepting += (_, e) => { e.Handled = true; OpenChange?.Invoke(this, EventArgs.Empty); };
            Add(open, new Label { X = Pos.Align(Alignment.Center), Y = y, Text = "·" });
        }
        var close = Hint("Esc close", y);
        close.Accepting += (_, e) => { e.Handled = true; Closed?.Invoke(this, EventArgs.Empty); };

        _alert = new AlertView(DialogWidth - 2) { X = 0, Y = y + 1 };
        if (!blame.IsCommitted) _alert.Show(NotCommitted, AlertSeverity.Info);
        Add(close, _alert);
        Height = y + 1 + _alert.Lines + 2;

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.BlameClose, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.BlameClose);
        // Bound either way, so Enter on an uncommitted line doesn't press the focused Esc hint.
        _scopeCommands.Register(CommandIds.BlameOpenChange, () =>
        {
            if (blame.IsCommitted) OpenChange?.Invoke(this, EventArgs.Empty);
        });
        _scopeKeybindings.Bind("Enter", CommandIds.BlameOpenChange);
    }

    internal string Location { get; }

    /// <summary>Hash, author and date; null for a line no commit has.</summary>
    internal string? Commit { get; }

    internal string? Subject { get; }

    internal string LineText { get; }

    internal string Status => _alert.Message;

    /// <summary><c>3 weeks ago (2026-09-04)</c>, the date in the author's own time zone.</summary>
    public static string When(DateTimeOffset date, DateTimeOffset now) =>
        $"{Ago(now - date)} ({date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})";

    /// <summary>Rounded as <c>git log --date=relative</c> rounds it, though a year is only ever years.</summary>
    internal static string Ago(TimeSpan elapsed)
    {
        var seconds = (long)Math.Max(0, elapsed.TotalSeconds);
        if (seconds < 90) return Plural(seconds, "second");
        var minutes = (seconds + 30) / 60;
        if (minutes < 90) return Plural(minutes, "minute");
        var hours = (minutes + 30) / 60;
        if (hours < 36) return Plural(hours, "hour");
        var days = (hours + 12) / 24;
        if (days < 14) return Plural(days, "day");
        if (days < 70) return Plural((days + 3) / 7, "week");
        if (days < 365) return Plural((days + 15) / 30, "month");
        return Plural((days + 183) / 365, "year");
    }

    private static string Plural(long count, string unit) => count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";

    private static Button Hint(string text, int y) => new()
    {
        Text = text,
        X = Pos.Align(Alignment.Center),
        Y = y,
        NoDecorations = true,
        NoPadding = true,
        ShadowStyle = ShadowStyles.None,
        HotKeySpecifier = (Rune)0xffff,
    };

    private static Label Row(string text, ref int y) => new() { X = 1, Y = y++, Width = Dim.Fill(1), Height = 1, Text = text };

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView)
    {
        base.OnHasFocusChanged(newHasFocus, previousFocusedView, focusedView);
        if (!newHasFocus) FocusLeft?.Invoke(this, EventArgs.Empty);
    }
}
