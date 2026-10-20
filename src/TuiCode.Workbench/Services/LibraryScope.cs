using MentalDesk.Tui.Keys;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

/// <summary>A library dialog's <see cref="IInputScope"/> on TuiCode's scope stack; it binds nothing of its own.</summary>
internal sealed class LibraryScope(IInputScope scope) : IKeybindingService
{
    public KeyHandlingResult Handle(Key key) => scope.Handle(key) switch
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
    public string? CurrentChord => scope.PendingChord;
    public event EventHandler<string?>? ChordChanged { add { } remove { } }
}
