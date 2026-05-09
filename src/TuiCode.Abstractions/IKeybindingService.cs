namespace TuiCode.Abstractions;

public enum KeyHandlingResult
{
    /// <summary>The key was not handled; let it propagate to the focused view.</summary>
    Pass,

    /// <summary>The key fired a command (or cancelled an in-progress chord); swallow it.</summary>
    Consumed,

    /// <summary>The key matched a chord prefix; we're waiting for the next key. Swallow it.</summary>
    ChordInProgress
}

public sealed record KeyBinding(string Sequence, string CommandId);

public enum KeybindingConflict
{
    /// <summary>Same key sequence already binds a different command. Replaceable.</summary>
    ExactMatch,

    /// <summary>Proposed sequence is a prefix of an existing chord (e.g. "Ctrl+W" while "Ctrl+W X" exists).</summary>
    PrefixOfExisting,

    /// <summary>Proposed sequence extends an existing binding (e.g. "Ctrl+W X" while "Ctrl+W" exists).</summary>
    ExtensionOfExisting
}

public interface IKeybindingService
{
    /// <summary>
    /// Bind a key sequence to a command. Sequences are space-separated, e.g.
    /// "Ctrl+S" for a single key, "Ctrl+W X" for a chord.
    /// </summary>
    void Bind(string keySequence, string commandId);

    /// <summary>Remove the binding at <paramref name="keySequence"/>. Returns true if a binding was removed.</summary>
    bool Unbind(string keySequence);

    /// <summary>Clear all bindings.</summary>
    void Reset();

    /// <summary>
    /// Report whether <paramref name="keySequence"/> would conflict with any existing binding,
    /// or null if it would not. Use before <see cref="Bind"/> to decide whether to surface a
    /// confirm/refuse dialog.
    /// </summary>
    KeybindingConflict? CheckConflict(string keySequence);

    /// <summary>All currently-registered bindings as (sequence, commandId) pairs.</summary>
    IEnumerable<KeyBinding> Bindings { get; }

    /// <summary>
    /// Process a key. Returns whether the key was consumed by the binding system,
    /// is in flight as part of a chord, or should be passed to the focused view.
    /// </summary>
    KeyHandlingResult Handle(Key key);

    /// <summary>The current chord prefix display (e.g. "Ctrl+W"), or null when idle.</summary>
    string? CurrentChord { get; }

    /// <summary>Fired when <see cref="CurrentChord"/> changes.</summary>
    event EventHandler<string?>? ChordChanged;
}
