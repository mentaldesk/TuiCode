using MentalDesk.Tui;
using MentalDesk.Tui.Dialogs;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.Git;

/// <summary>
/// Modal picker for Compare to revision (<c>ctr</c>): a filter over the file's branches, tags and
/// recent commits, which load after it opens. Enter picks the highlighted row, or the typed text when
/// nothing matches; the host reads the file at it and closes the picker with <see cref="PickerDialog{T}.Choose"/>.
/// </summary>
public sealed class RevisionPickerView : PickerDialog<RevisionEntry>
{
    private const int DialogWidth = 70;

    public RevisionPickerView(string fileName)
        : base($"Compare {fileName} to revision", DialogWidth, 20, "Loading branches, tags and commits…",
            e => e.Subject is null ? e.Revision : $"{e.Revision} {e.Subject}", "Compare",
            e => e.Display(PickerList.Width(DialogWidth)))
    {
        Commands.Register(PickId, "Compare", Pick);
        Commands.Register(CancelId, "Cancel", ClearOrCancel);
        FilterField.TextChanged += (_, _) => SelectedIndex = RevisionList.Selected(Shown, FilterField.Text);
    }

    /// <summary>The row or typed revision to compare with; the picker stays open until the host chooses it.</summary>
    public event EventHandler<RevisionEntry>? Comparing;

    internal IReadOnlyList<string> VisibleItems => [.. Shown.Select(e => e.Text)];

    public void Load(IReadOnlyList<GitRef> refs, IReadOnlyList<GitCommit> commits) =>
        ShowRows(RevisionList.Build(refs, commits), "");

    public void ShowLoadFailed(string message, Action? retry)
    {
        if (retry is null) ShowRows([], message);
        else ShowFailed(message, retry);
    }

    public void ShowError(string message) => ShowAlert(message, Severity.Error);

    private void Pick()
    {
        var typed = FilterField.Text.Trim();
        var revision = SelectedIndex is { } i && i < Shown.Count ? Shown[i]
            : typed.Length > 0 ? RevisionEntry.Typed(typed)
            : null;
        if (revision is not null) Comparing?.Invoke(this, revision);
    }

    private void ClearOrCancel()
    {
        if (FilterField.Text.Length > 0) FilterField.Text = "";
        else Cancel();
    }
}
