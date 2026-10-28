using MentalDesk.Tui;
using MentalDesk.Tui.Dialogs;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.Git;

/// <summary>
/// Modal picker for Open pull request (<c>opr</c>): a filter over the repo's open PRs, which load after it
/// opens (#525). Enter checks the highlighted one out in its own worktree; the host cancels the pick, does the
/// work, and then chooses the PR or says why not with <see cref="ShowError"/>.
/// </summary>
public sealed class PullRequestPickerView : PickerDialog<GitHubPullRequestSummary>
{
    private const int DialogWidth = 78;
    private const int ContentRows = 20;

    public PullRequestPickerView()
        : base("Open pull request", DialogWidth, ContentRows, "Loading pull requests…",
            pr => $"#{pr.Number} {pr.Title} {pr.Author}", "Check out",
            pr => PullRequestList.Display(pr, PickerList.Width(DialogWidth)))
    {
        Add(new VerbatimLabel { X = 1, Y = ContentRows - 1, Text = Legend, Enabled = false });
        Commands.Register(CancelId, "Cancel", ClearOrCancel);
    }

    internal const string Legend = $"{PullRequestList.RequestedMarker} your review is requested";

    internal IReadOnlyList<string> VisibleItems => [.. Shown.Select(pr => $"#{pr.Number}")];

    public void Load(IReadOnlyList<GitHubPullRequestSummary> pullRequests) => ShowRows(pullRequests, "No open pull requests");

    public void ShowLoadFailed(string message, Action? retry)
    {
        if (retry is null) ShowRows([], message);
        else ShowFailed(message, retry);
    }

    /// <summary>Shows why the checkout didn't happen; the picker stays open so another PR can be picked.</summary>
    public void ShowError(string message) => ShowAlert(message, Severity.Error);

    /// <summary>Shows what the host is doing while it checks a PR out.</summary>
    public void ShowBusy(int number) => ShowAlert($"Checking out #{number}…", Severity.Info);

    private void ClearOrCancel()
    {
        if (FilterField.Text.Length > 0) FilterField.Text = "";
        else Cancel();
    }
}
