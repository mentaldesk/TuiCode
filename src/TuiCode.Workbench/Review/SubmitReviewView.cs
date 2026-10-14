using AppButton = MentalDesk.Tui.Dialogs.AppButton;
using MentalDesk.Tui.Loading;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Review;

/// <summary>
/// Modal for Submit review (<c>sr</c>): the verdict, a summary, and Submit and Cancel. It opens while the host looks the branch's PR up,
/// which it hands over with <see cref="Load"/> (#525). The host posts the review and reports back through <see cref="ShowError"/>, so a refused review
/// keeps the dialog and its text.
/// </summary>
public sealed class SubmitReviewView : Window
{
    private const string MissingSummary = "Comment and Request changes need a summary.";
    private const int DialogWidth = 70;
    private const int DialogHeight = 16 + InputView.Frame;

    private readonly OptionSelector<GitHubReviewVerdict> _verdict;
    private readonly InputView _summary;
    private readonly LoadStateView _state;
    private readonly VerbatimLabel _drafts;
    private readonly Button[] _buttons;
    private readonly AlertView _alert;
    private readonly Button _submit;
    private Action? _retry;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    /// <summary>The review to post; the host does the work and closes the dialog once GitHub has it.</summary>
    public event EventHandler<(GitHubReviewVerdict Verdict, string Summary)>? Submitted;

    public SubmitReviewView()
    {
        Title = "Submit review";
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

        _drafts = new VerbatimLabel { X = 1, Y = Pos.AnchorEnd(2), Text = string.Empty, Visible = false };

        _summary = new InputView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(),
            CanFocus = false,
        };
        var form = new View { Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        form.Add(_verdict, _summary);
        _state = new LoadStateView(form, "Loading pull request…") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(Foot) };

        _submit = Place(AppButton.Primary("Submit"), 1, OnSubmit);
        _submit.Enabled = false;
        _submit.Visible = false;
        var cancel = Place(AppButton.Secondary("Cancel"), Pos.Right(_submit) + 1, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _buttons = [_submit, cancel];

        // Below the buttons, not beside them: the dialog grows for an alert rather than the summary shrinking.
        _alert = new AlertView(DialogWidth - 2) { X = 0, Y = Pos.AnchorEnd() };

        Add(_state, _drafts, _submit, cancel, _alert);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.SubmitReviewCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.SubmitReviewConfirm, OnSubmit);
        _scopeKeybindings.Bind("Esc", CommandIds.SubmitReviewCancel);
        _scopeKeybindings.Bind("Ctrl+Enter", CommandIds.SubmitReviewConfirm);
        _scopeCommands.Register(CommandIds.SubmitReviewRetry, "Retry", () => _retry?.Invoke(), CommandScope.Global,
            () => _retry is not null && _state.State == LoadState.Failed);
        _scopeKeybindings.Bind("r", CommandIds.SubmitReviewRetry);
    }

    private static Button Place(Button button, Pos x, Action accepted)
    {
        button.X = x;
        button.Y = Pos.AnchorEnd(1);
        button.Accepting += (_, e) => { e.Handled = true; accepted(); };
        return button;
    }

    public GitHubReviewVerdict Verdict => _verdict.Value ?? GitHubReviewVerdict.Comment;

    public string Summary => _summary.Text ?? string.Empty;

    internal string Status => _alert.Message;

    internal string DraftsText => _drafts.Text;

    internal LoadStateView State => _state;
    internal InputView SummaryBox => _summary;
    internal bool Loaded => _state.State == LoadState.Loaded;

    public void ShowLoading()
    {
        _retry = null;
        _state.ShowLoading();
    }

    /// <param name="drafts">Draft line comments (#188) that go with the review; said so above the buttons.</param>
    public void Load(int number, int drafts)
    {
        Title = $"Submit review on #{number}";
        _drafts.Text = DraftComments.Posting(drafts);
        _drafts.Visible = drafts > 0;
        _submit.Enabled = true;
        _submit.Visible = true;
        _summary.CanFocus = true;
        _state.ShowContent();
        _summary.SetFocus();
        LayoutFoot();
    }

    public void ShowNoPullRequest() => ShowLoadFailed("No pull request for this branch", null);

    public void ShowLoadFailed(string message, Action? retry)
    {
        _retry = retry;
        _state.ShowLoadFailed(message, retry);
    }

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

    /// <summary>Rows under the summary: the buttons, and the drafts line when there is one.</summary>
    private int Foot => _drafts.Visible ? 3 : 2;

    private void Alert(string message, AlertSeverity severity)
    {
        _alert.Show(message, severity);
        LayoutFoot();
    }

    private void LayoutFoot()
    {
        Height = DialogHeight + _alert.Lines;
        _state.Height = Dim.Fill(_alert.Lines + Foot);
        foreach (var button in _buttons) button.Y = Pos.AnchorEnd(_alert.Lines + 1);
        if (_drafts.Visible) _drafts.Y = Pos.AnchorEnd(_alert.Lines + 2);
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
