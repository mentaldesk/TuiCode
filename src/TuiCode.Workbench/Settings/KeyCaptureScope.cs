using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// An <see cref="IKeybindingService"/> that doesn't bind anything: every key it sees is routed
/// to <paramref name="onKey"/>. Pushed onto the scope stack while the keybindings picker is
/// capturing a key combination from the user, so workbench/settings shortcuts don't leak in.
/// </summary>
internal sealed class KeyCaptureScope : IKeybindingService
{
    private readonly Action<Key> _onKey;

    public KeyCaptureScope(Action<Key> onKey)
    {
        ArgumentNullException.ThrowIfNull(onKey);
        _onKey = onKey;
    }

    public KeyHandlingResult Handle(Key key)
    {
        _onKey(key);
        return KeyHandlingResult.Consumed;
    }

    public void Bind(string keySequence, string commandId) { }
    public bool Unbind(string keySequence) => false;
    public void Reset() { }
    public KeybindingConflict? CheckConflict(string keySequence) => null;
    public IEnumerable<KeyBinding> Bindings => Array.Empty<KeyBinding>();
    public string? CurrentChord => null;
    public event EventHandler<string?>? ChordChanged { add { } remove { } }
}
