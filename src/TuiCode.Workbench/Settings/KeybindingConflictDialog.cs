using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Tiny modal for keybinding conflict prompts. Hosted as a child view of the picker so it
/// renders on top of it without us needing TG's modal-dialog plumbing.
/// </summary>
internal static class KeybindingConflictDialog
{
    public static void ShowReplace(View host, IInputScopeStack scopes, string sequence, string existingCommandLabel, Action<bool> onChoice)
    {
        var dialog = new Dialog
        {
            Title = "Replace binding?",
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 60,
            Height = 9
        };

        var label = new Label
        {
            X = 1,
            Y = 1,
            Text = $"{sequence} is already bound to {existingCommandLabel}.\nReplace it?"
        };

        var yes = new Button { Text = "Replace", X = Pos.Center() - 12, Y = Pos.AnchorEnd(2), IsDefault = true };
        var no = new Button { Text = "Cancel", X = Pos.Center() + 2, Y = Pos.AnchorEnd(2) };

        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);

        void Close(bool replace)
        {
            scopes.Pop(keybindings);
            host.Remove(dialog);
            dialog.Dispose();
            onChoice(replace);
        }

        yes.Accepting += (_, _) => Close(true);
        no.Accepting += (_, _) => Close(false);

        commands.Register("conflict.confirm", () => Close(true));
        commands.Register("conflict.cancel", () => Close(false));
        keybindings.Bind("Esc", "conflict.cancel");

        dialog.Add(label, yes, no);
        scopes.Push(keybindings);
        host.Add(dialog);
        no.SetFocus();
    }

    public static void ShowChord(View host, IInputScopeStack scopes, string sequence)
    {
        var dialog = new Dialog
        {
            Title = "Conflict",
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 60,
            Height = 9
        };

        var label = new Label
        {
            X = 1,
            Y = 1,
            Text = $"{sequence} conflicts with an existing chord.\nRemove the existing binding first."
        };

        var ok = new Button { Text = "OK", X = Pos.Center(), Y = Pos.AnchorEnd(2), IsDefault = true };

        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);

        void Close()
        {
            scopes.Pop(keybindings);
            host.Remove(dialog);
            dialog.Dispose();
        }

        ok.Accepting += (_, _) => Close();
        commands.Register("conflict.dismiss", Close);
        keybindings.Bind("Esc", "conflict.dismiss");

        dialog.Add(label, ok);
        scopes.Push(keybindings);
        host.Add(dialog);
        ok.SetFocus();
    }
}
