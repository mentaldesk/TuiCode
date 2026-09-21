using System.Text;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Review;

/// <summary>
/// Modal for Submit review (<c>sr</c>): the verdict, a summary, and the two hints, which are themselves the
/// buttons. The host posts the review and reports back through <see cref="ShowError"/>, so a refused review
/// keeps the dialog and its text.
/// </summary>
public sealed class SubmitReviewView : Window
{
    private const string MissingSummary = "Comment and Request changes need a summary.";
    private const int DialogWidth = 70;
    private const int DialogHeight = 16;

    private readonly OptionSelector<GitHubReviewVerdict> _verdict;
    private readonly TextView _summary;
    private readonly View[] _hints;
    private readonly AlertView _alert;
    private readonly Button _submit;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    /// <summary>The review to post; the host does the work and closes the dialog once GitHub has it.</summary>
    public event EventHandler<(GitHubReviewVerdict Verdict, string Summary)>? Submitted;

    public SubmitReviewView(int number)
    {
        Title = $"Submit review on #{number}";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = DialogHeight;
        CanFocus = true;

        _verdict = new OptionSelector<GitHubReviewVerdict>
        {
            X = 1,
            Y = 0,
            Orientation = Orientation.Horizontal,
            TabBehavior = TabBehavior.NoStop,
            Labels = ["Comment", "Approve", "Request changes"],
            Value = GitHubReviewVerdict.Comment,
        };

        _summary = new TextView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            // Otherwise Tab types a tab here instead of moving on to the hints.
            TabKeyAddsTab = false,
            WordWrap = true,
        };

        _submit = Hint("Ctrl+Enter submit", 1);
        var separator = new Label { X = Pos.Right(_submit) + 1, Y = Pos.AnchorEnd(1), Text = "·" };
        var cancel = Hint("Esc cancel", Pos.Right(separator) + 1);
        _submit.Accepting += (_, e) => { e.Handled = true; OnSubmit(); };
        cancel.Accepting += (_, e) => { e.Handled = true; Cancelled?.Invoke(this, EventArgs.Empty); };
        _hints = [_submit, separator, cancel];

        // Below the hints, not beside them: the dialog grows for an alert rather than the summary shrinking.
        _alert = new AlertView(DialogWidth - 2) { X = 0, Y = Pos.AnchorEnd() };

        Add(_verdict, _summary, _submit, separator, cancel, _alert);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.SubmitReviewCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.SubmitReviewConfirm, OnSubmit);
        _scopeKeybindings.Bind("Esc", CommandIds.SubmitReviewCancel);
        _scopeKeybindings.Bind("Ctrl+Enter", CommandIds.SubmitReviewConfirm);
    }

    /// <summary>A hint that is its own button: plain text, clickable, and no hotkey of its own.</summary>
    private static Button Hint(string text, Pos x) => new()
    {
        Text = text,
        X = x,
        Y = Pos.AnchorEnd(1),
        NoDecorations = true,
        NoPadding = true,
        ShadowStyle = ShadowStyles.None,
        HotKeySpecifier = (Rune)0xffff,
    };

    public GitHubReviewVerdict Verdict => _verdict.Value ?? GitHubReviewVerdict.Comment;

    public string Summary => _summary.Text ?? string.Empty;

    internal string Status => _alert.Message;

    public bool FocusSummary() => _summary.SetFocus();

    /// <summary>Shows why GitHub refused the review; the dialog stays open with whatever was typed.</summary>
    public void ShowError(string message)
    {
        Alert(message, AlertSeverity.Error);
        _submit.Enabled = true;
    }

    /// <summary>Shows what the host is doing, and refuses a second submit while it does it.</summary>
    public void ShowBusy()
    {
        Alert("Submitting…", AlertSeverity.Info);
        _submit.Enabled = false;
    }

    private void Alert(string message, AlertSeverity severity)
    {
        _alert.Show(message, severity);
        Height = DialogHeight + _alert.Lines;
        _summary.Height = Dim.Fill(_alert.Lines + 2);
        foreach (var hint in _hints) hint.Y = Pos.AnchorEnd(_alert.Lines + 1);
        SetNeedsLayout();
    }

    private void OnSubmit()
    {
        if (!_submit.Enabled) return;

        var summary = Summary.Trim();
        if (summary.Length == 0 && Verdict != GitHubReviewVerdict.Approve)
        {
            Alert(MissingSummary, AlertSeverity.Error);
            _summary.SetFocus();
            return;
        }
        Submitted?.Invoke(this, (Verdict, summary));
    }
}
