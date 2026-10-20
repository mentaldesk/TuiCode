using MentalDesk.Tui.Dialogs;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Tiny modal saying a binding can't be added. Hosted as a child view of the settings view so it
/// renders on top of it without us needing TG's modal-dialog plumbing.
/// </summary>
internal static class KeybindingRefusal
{
    public static void Show(View host, IInputScopeStack scopes, string message, Action onClose)
    {
        var dialog = Create("Conflict", message);

        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);

        void Close()
        {
            scopes.Pop(keybindings);
            host.Remove(dialog);
            dialog.Dispose();
            onClose();
        }

        commands.Register("conflict.dismiss", Close);
        keybindings.Bind("Esc", "conflict.dismiss");

        var ok = KeyedButton.Of(ButtonKind.Primary, keybindings, "conflict.dismiss", "OK");
        ok.X = Pos.Center();
        ok.Y = Pos.AnchorEnd(2);
        ok.Accepting += (_, _) => Close();

        dialog.Add(ok);
        scopes.Push(keybindings);
        host.Add(dialog);
        ok.SetFocus();
    }

    private static Dialog Create(string title, string message)
    {
        var dialog = new Dialog
        {
            Title = title,
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 64,
            Height = 7 + message.Split('\n').Length
        };
        dialog.Add(new VerbatimLabel { X = 1, Y = 1, Text = message });
        return dialog;
    }
}
