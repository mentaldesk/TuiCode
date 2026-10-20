using MentalDesk.Tui.Dialogs;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Workbench;

namespace TuiCode.Tests;

internal static class ConfirmDialogs
{
    public static readonly Key CtrlEnter = Key.Enter.WithCtrl;

    public static ConfirmDialog? Of(View host) => host.SubViews.OfType<ConfirmDialog>().SingleOrDefault();

    public static string Message(this ConfirmDialog dialog) => string.Join('\n', dialog.Lines);

    /// <summary>Each button as it reads on screen, key included: <c>Ctrl+Enter Save</c>, <c>Don't save</c>, <c>Esc Cancel</c>.</summary>
    public static string[] Buttons(this ConfirmDialog dialog) => [.. dialog.ButtonRow.Select(Caption)];

    public static string[] Kinds(this ConfirmDialog dialog) =>
        [.. dialog.ButtonRow.Select(b => b.SchemeName!.Replace("Button", "", StringComparison.Ordinal))];

    public static string? FocusedButton(this ConfirmDialog dialog) =>
        dialog.ButtonRow.FirstOrDefault(b => b.HasFocus) is { } button ? Caption(button) : null;

    public static string Caption(Button button) => button.Text.Replace("_", "", StringComparison.Ordinal).Trim();

    /// <summary>The key that takes the button whose label (without its key) is <paramref name="label"/>.</summary>
    public static Key KeyFor(this ConfirmDialog dialog, string label)
    {
        var button = dialog.ButtonRow.Single(b => Caption(b) == label || Caption(b).EndsWith($" {label}", StringComparison.Ordinal));
        if (button == dialog.CancelButton) return Key.Esc;
        if (Caption(button).StartsWith("Ctrl+Enter ", StringComparison.Ordinal)) return CtrlEnter;
        return new Key((KeyCode)char.ToUpperInvariant(button.Text[button.Text.IndexOf('_') + 1]));
    }

    /// <summary>A host step that takes the open confirm's <paramref name="label"/> by its key.</summary>
    public static Action Choose(WorkbenchHost host, View workbench, string label) =>
        () => host.App.InjectKey(Of(workbench)!.KeyFor(label));
}
