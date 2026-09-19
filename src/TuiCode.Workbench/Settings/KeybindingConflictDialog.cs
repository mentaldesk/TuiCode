using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Tiny modal for keybinding conflict prompts. Hosted as a child view of the settings view so it
/// renders on top of it without us needing TG's modal-dialog plumbing.
/// </summary>
internal static class KeybindingConflictDialog
{
    public static void Confirm(View host, IInputScopeStack scopes, string message, string confirmText, Action<bool> onChoice)
    {
        var dialog = Create("Shortcut in use", message);

        var yes = new Button { Text = confirmText, X = Pos.Center() - 12, Y = Pos.AnchorEnd(2) };
        var no = new Button { Text = "Cancel", X = Pos.Center() + 2, Y = Pos.AnchorEnd(2), IsDefault = true };

        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);

        void Close(bool confirmed)
        {
            scopes.Pop(keybindings);
            host.Remove(dialog);
            dialog.Dispose();
            onChoice(confirmed);
        }

        yes.Accepting += (_, _) => Close(true);
        no.Accepting += (_, _) => Close(false);

        commands.Register("conflict.confirm", () => Close(true));
        commands.Register("conflict.cancel", () => Close(false));
        keybindings.Bind("Esc", "conflict.cancel");

        dialog.Add(yes, no);
        scopes.Push(keybindings);
        host.Add(dialog);
        no.SetFocus();
    }

    public static void Refuse(View host, IInputScopeStack scopes, string message, Action onClose)
    {
        var dialog = Create("Conflict", message);

        var ok = new Button { Text = "OK", X = Pos.Center(), Y = Pos.AnchorEnd(2), IsDefault = true };

        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);

        void Close()
        {
            scopes.Pop(keybindings);
            host.Remove(dialog);
            dialog.Dispose();
            onClose();
        }

        ok.Accepting += (_, _) => Close();
        commands.Register("conflict.dismiss", Close);
        keybindings.Bind("Esc", "conflict.dismiss");

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
        dialog.Add(new Label { X = 1, Y = 1, Text = message });
        return dialog;
    }
}
