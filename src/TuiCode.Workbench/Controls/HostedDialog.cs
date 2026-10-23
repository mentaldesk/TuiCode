using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Keys;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Controls;

/// <summary>A library <see cref="AppDialog"/> added to <c>host</c> with its keys on our scope stack, and <c>F1</c> showing its keys.</summary>
internal static class HostedDialog
{
    /// <summary><paramref name="closed"/> runs once the dialog has left <paramref name="host"/>, before it's disposed.</summary>
    public static void Show(View host, IInputScopeStack scopes, AppDialog dialog, Action closed)
    {
        var scope = new LibraryScope(dialog.Scope);
        dialog.KeysRequested += (_, sheet) => ShowKeys(host, scopes, dialog, sheet);
        dialog.Closed += (_, _) =>
        {
            scopes.Pop(scope);
            host.Remove(dialog);
            closed();
            dialog.Dispose();
        };
        host.Add(dialog);
        scopes.Push(scope);
    }

    private static void ShowKeys(View host, IInputScopeStack scopes, AppDialog dialog, KeySheet sheet)
    {
        var focused = dialog.MostFocused;
        var caret = (focused as TextField)?.InsertionPoint;
        var keys = new KeysDialog(sheet, host.Viewport.Size);
        var scope = new LibraryScope(keys.Scope);
        keys.Closed += (_, _) =>
        {
            scopes.Pop(scope);
            host.Remove(keys);
            keys.Dispose();
            focused?.SetFocus();
            // A TextField selects all as it takes focus, so the next key would replace what was typed.
            if (focused is TextField field && caret is { } at)
            {
                field.ClearAllSelection();
                field.InsertionPoint = at;
            }
        };
        host.Add(keys);
        scopes.Push(scope);
        keys.SetFocus();
    }
}
