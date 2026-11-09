using MentalDesk.Tui;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using MentalDesk.Tui.Loading;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.Review;

/// <summary>
/// Modal for Submit review (<c>sr</c>): the verdict, a summary, and Submit and Cancel. It opens while the host looks the branch's PR up,
/// which it hands over with <see cref="Load"/> (#525). The host posts the review and reports back through <see cref="ShowError"/>, so a refused review
/// keeps the dialog and its text.
/// </summary>
public sealed class SubmitReviewView : AppDialog
{
    private const int DialogWidth = 70;
    private const int ContentRows = 14 + MultiLineBox.Frame;

    private readonly OptionSelector<GitHubReviewVerdict> _verdict;
    private readonly VerbatimLabel _summaryLabel;
    private readonly MultiLineField _summary;
    private readonly LoadStateView _state;
    private readonly VerbatimLabel _drafts;
    private readonly Button _submit;
    private Action? _retry;

    /// <summary>The review to post; the host does the work and closes the dialog once GitHub has it.</summary>
    public event EventHandler<(GitHubReviewVerdict Verdict, string Summary)>? Submitted;

    public SubmitReviewView()
        : base("Submit review", DialogWidth, ContentRows)
    {
        _verdict = new OptionSelector<GitHubReviewVerdict>
        {
            X = 1,
            Y = 0,
            Orientation = Orientation.Horizontal,
            Labels = ["Comment", "Approve", "Request changes"],
            Value = GitHubReviewVerdict.Comment,
        }.SelectOnArrows();

        _drafts = new VerbatimLabel { X = 1, Y = ContentRows - 2, Text = string.Empty, Visible = false };

        _summaryLabel = new VerbatimLabel { X = 1, Y = 1, Text = SummaryName };
        _verdict.ValueChanged += (_, _) =>
        {
            _summaryLabel.Text = SummaryName;
            Recheck();
        };
        _summary = new MultiLineField
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(),
            CanFocus = false,
        }.HeavyWhileFocused();
        var form = new View { Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        form.Add(_verdict, _summaryLabel, _summary);
        _state = new LoadStateView(form, "Loading pull request…") { X = 0, Y = 0, Width = Dim.Fill(), Height = ContentRows - Foot };
        Add(_state, _drafts);
        Check(_summary, _summaryLabel, () =>
            string.IsNullOrWhiteSpace(Summary) && Verdict != GitHubReviewVerdict.Approve ? "Summary is needed for Comment and Request changes" : null);

        Commands.Register(CommandIds.SubmitReviewConfirm, "Submit", OnSubmit);
        Keys.Bind("Ctrl+Enter", CommandIds.SubmitReviewConfirm);
        Commands.Register(CommandIds.SubmitReviewRetry, "Retry", () => _retry?.Invoke(),
            isEnabled: () => _retry is not null && _state.State == LoadState.Failed);
        Keys.Bind("r", CommandIds.SubmitReviewRetry);

        _submit = CommandButton(ButtonKind.Primary, CommandIds.SubmitReviewConfirm);
        _submit.IsDefault = false;
        _submit.Enabled = false;
        _submit.Visible = false;
        AddButtonRow(ContentRows - 1, _submit, CommandButton(ButtonKind.Secondary, CancelId));
    }

    public GitHubReviewVerdict Verdict => _verdict.Value ?? GitHubReviewVerdict.Comment;

    public string Summary => _summary.Text ?? string.Empty;

    internal VerbatimLabel SummaryLabel => _summaryLabel;

    internal string DraftsText => _drafts.Text;

    internal LoadStateView State => _state;
    internal MultiLineField SummaryBox => _summary;
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
        _state.Height = ContentRows - Foot;
    }

    public void ShowNoPullRequest() => ShowLoadFailed("No pull request for this branch", null);

    public void ShowLoadFailed(string message, Action? retry)
    {
        _retry = retry;
        _state.ShowLoadFailed(message, retry);
    }

    public bool FocusSummary() => _summary.SetFocus();

    /// <summary>Marks the summary with why GitHub refused the review; the dialog stays open with whatever was typed.</summary>
    public void ShowError(string message)
    {
        _submit.Enabled = true;
        Refuse(_summary, $"Summary: {message}");
    }

    /// <summary>Shows what the host is doing, and refuses a second submit while it does it.</summary>
    public void ShowBusy()
    {
        ShowAlert("Submitting…", Severity.Info);
        _submit.Enabled = false;
    }

    /// <summary>Closes the dialog once the host has done what it was asked.</summary>
    public void Finish() => Accept();

    private string SummaryName => Verdict == GitHubReviewVerdict.Approve ? "Summary (optional)" : "Summary";

    /// <summary>Rows under the summary: the buttons, and the drafts line when there is one.</summary>
    private int Foot => _drafts.Visible ? 3 : 2;

    private void OnSubmit()
    {
        if (_submit.Enabled && Checked()) Submitted?.Invoke(this, (Verdict, Summary.Trim()));
    }
}
