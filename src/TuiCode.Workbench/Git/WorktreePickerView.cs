using MentalDesk.Tui.Dialogs;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.Git;

/// <summary>Modal picker for Open worktree (<c>ow</c>): a filter over the repo's other worktrees, under a column header.</summary>
public sealed class WorktreePickerView : PickerDialog<WorktreeRow>
{
    private const int DialogWidth = 78;

    public WorktreePickerView(IReadOnlyList<WorktreeRow> worktrees)
        : base("Open worktree", DialogWidth, 16, worktrees, w => $"{w.Branch} {w.Name} {w.Parent}", "Open",
            w => WorktreeList.Display(w, worktrees, PickerList.Width(DialogWidth)),
            WorktreeList.Header(worktrees, PickerList.Width(DialogWidth)))
    {
        Header = WorktreeList.Header(worktrees, PickerList.Width(DialogWidth));
    }

    internal string Header { get; }

    internal IReadOnlyList<string> VisibleItems => [.. Shown.Select(w => w.Name)];
}
