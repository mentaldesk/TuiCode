using AppButton = MentalDesk.Tui.Dialogs.AppButton;
using ButtonKind = MentalDesk.Tui.Dialogs.ButtonKind;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Controls;

/// <summary>A dialog button with its key written first (<c>Ctrl+Enter Submit</c>), read from the dialog's own binding so the two can't drift.</summary>
internal static class KeyedButton
{
    public static Button Of(ButtonKind kind, IKeybindingService keys, string commandId, string label)
    {
        var caption = Caption(keys, commandId, label);
        return kind switch
        {
            ButtonKind.Primary => AppButton.Primary(caption),
            ButtonKind.Danger => AppButton.Danger(caption),
            _ => AppButton.Secondary(caption),
        };
    }

    /// <summary>The command's first binding names the key, so bind the one to show before any other.</summary>
    internal static string Caption(IKeybindingService keys, string commandId, string label) =>
        $"{keys.Bindings.First(b => b.CommandId == commandId).Display} {label}";
}
