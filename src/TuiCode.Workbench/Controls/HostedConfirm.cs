using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Keys;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Controls;

/// <summary>The library's <see cref="ConfirmDialog"/>, added to <c>host</c> with its keys on our scope stack.</summary>
internal static class HostedConfirm
{
    public static readonly Key MainKey = Key.Enter.WithCtrl;

    /// <summary><paramref name="closed"/> gets the action taken, or null for Cancel, once the dialog has gone.</summary>
    public static ConfirmDialog Show(View host, IInputScopeStack scopes, string title, string message,
        IReadOnlyList<ConfirmAction> actions, Action<ConfirmAction?> closed, string cancel = "Cancel")
    {
        var dialog = new ConfirmDialog(title, message.Split('\n'), actions, cancel: cancel);
        // ConfirmDialog sizes itself two columns short of its button row, which then runs off the left edge.
        if (dialog.Width is DimAbsolute width)
            dialog.Width = width.Size + 2;
        foreach (var line in dialog.SubViews.OfType<Label>().Where(l => dialog.Lines.Contains(l.Text)))
        {
            var text = line.Text;
            line.HotKeySpecifier = VerbatimLabel.NoHotKey;
            line.Text = text;
        }

        var scope = new Scope(dialog.Scope);
        dialog.Closed += (_, _) =>
        {
            scopes.Pop(scope);
            host.Remove(dialog);
            var chosen = dialog.Chosen;
            dialog.Dispose();
            closed(chosen);
        };
        host.Add(dialog);
        scopes.Push(scope);
        dialog.SetFocus();
        return dialog;
    }

    private sealed class Scope(IInputScope dialog) : IKeybindingService
    {
        public KeyHandlingResult Handle(Key key) => dialog.Handle(key) switch
        {
            KeyResult.Consumed => KeyHandlingResult.Consumed,
            KeyResult.ChordInProgress => KeyHandlingResult.ChordInProgress,
            _ => KeyHandlingResult.Pass,
        };

        public void Bind(string keySequence, string commandId) { }
        public void Bind(IReadOnlyList<Key> chord, string commandId) { }
        public bool Unbind(string keySequence, CommandScope scope = CommandScope.Global) => false;
        public bool Unbind(IReadOnlyList<Key> chord, CommandScope scope = CommandScope.Global, string? commandId = null) => false;
        public void Reset() { }
        public KeybindingConflict? CheckConflict(string keySequence, CommandScope scope = CommandScope.Global) => null;
        public KeybindingConflict? CheckConflict(IReadOnlyList<Key> chord, CommandScope scope = CommandScope.Global) => null;
        public Func<CommandScope> FocusedScope { get; set; } = () => CommandScope.Global;
        public IEnumerable<KeyBinding> Bindings => [];
        public string? CurrentChord => dialog.PendingChord;
        public event EventHandler<string?>? ChordChanged { add { } remove { } }
    }
}
