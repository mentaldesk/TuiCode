using MentalDesk.Tui.Dialogs;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.Workspace;

/// <summary>Modal picker for Open recent folder (<c>or</c>): a filter over the folders you've had open.</summary>
public sealed class RecentFolderPickerView(IReadOnlyList<RecentFolder> folders) : PickerDialog<RecentFolder>(
    "Open recent folder", DialogWidth, 14, folders, f => $"{f.Name} {f.Parent}", "Open",
    f => RecentFolderList.Display(f, folders.Max(r => r.Name.Length), PickerList.Width(DialogWidth)))
{
    private const int DialogWidth = 78;

    internal IReadOnlyList<string> VisibleItems => [.. Shown.Select(f => f.Name)];

    internal int? SelectedItem => this.SelectedIndex();
}
