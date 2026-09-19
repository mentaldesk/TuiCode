using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

/// <summary>
/// A non-modal input scope: its own bindings get first look at a key — but only while
/// <paramref name="accepts"/> says so (typically "my panel has focus") — and everything else falls
/// through to the scope beneath. Used by the find bar, which needs a few keys of its own (Enter, Esc,
/// Tab) without shadowing workbench shortcuts the way a pushed modal scope does. A chord in flight in the scope beneath always finishes there.
/// </summary>
internal sealed class LayeredScope : IKeybindingService
{
    private readonly IKeybindingService _own;
    private readonly IKeybindingService _below;
    private readonly Func<Key, bool> _accepts;

    public LayeredScope(IKeybindingService own, IKeybindingService below, Func<Key, bool> accepts)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(below);
        ArgumentNullException.ThrowIfNull(accepts);
        _own = own;
        _below = below;
        _accepts = accepts;
    }

    public KeyHandlingResult Handle(Key key)
    {
        if (_below.CurrentChord is null && _accepts(key))
        {
            var result = _own.Handle(key);
            if (result != KeyHandlingResult.Pass) return result;
        }
        return _below.Handle(key);
    }

    public void Bind(string keySequence, string commandId) => _own.Bind(keySequence, commandId);
    public void Bind(IReadOnlyList<Key> chord, string commandId) => _own.Bind(chord, commandId);
    public bool Unbind(string keySequence, CommandScope scope = CommandScope.Global) => _own.Unbind(keySequence, scope);
    public bool Unbind(IReadOnlyList<Key> chord, CommandScope scope = CommandScope.Global) => _own.Unbind(chord, scope);
    public void Reset() => _own.Reset();
    public KeybindingConflict? CheckConflict(string keySequence, CommandScope scope = CommandScope.Global) => _own.CheckConflict(keySequence, scope);
    public KeybindingConflict? CheckConflict(IReadOnlyList<Key> chord, CommandScope scope = CommandScope.Global) => _own.CheckConflict(chord, scope);
    public Func<CommandScope> FocusedScope { get => _own.FocusedScope; set => _own.FocusedScope = value; }
    public IEnumerable<KeyBinding> Bindings => _own.Bindings;

    // Chords only ever live in the scope beneath (the layer's own bindings are single keys).
    public string? CurrentChord => _below.CurrentChord;

    public event EventHandler<string?>? ChordChanged
    {
        add => _below.ChordChanged += value;
        remove => _below.ChordChanged -= value;
    }
}
