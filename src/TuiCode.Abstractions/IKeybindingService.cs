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

/// <summary>
/// A bound chord and the command it fires. The chord is the canonical identity — a sequence of
/// <see cref="Key"/> compared by raw <see cref="KeyCode"/>, not by display string (issue #89).
/// <see cref="Display"/> is the human-readable label and must never be parsed back into keys.
/// </summary>
public sealed class KeyBinding : IEquatable<KeyBinding>
{
    public KeyBinding(IReadOnlyList<Key> chord, string commandId)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.Count == 0) throw new ArgumentException("A chord needs at least one key.", nameof(chord));
        ArgumentException.ThrowIfNullOrEmpty(commandId);
        Chord = chord;
        CommandId = commandId;
    }

    public IReadOnlyList<Key> Chord { get; }
    public string CommandId { get; }

    /// <summary>Parse-stable identity (keycode-based). Use for equality, dedup, and diffing.</summary>
    public string CanonicalId => KeyChord.Canonical(Chord);

    /// <summary>Human-readable label (e.g. <c>"Ctrl+W x"</c>) — UI only, never parsed back.</summary>
    public string Display => KeyChord.Display(Chord);

    public bool Equals(KeyBinding? other) =>
        other is not null
        && string.Equals(CommandId, other.CommandId, StringComparison.Ordinal)
        && string.Equals(CanonicalId, other.CanonicalId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as KeyBinding);

    public override int GetHashCode() => HashCode.Combine(CanonicalId, CommandId);
}

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
    /// <summary>Bind a chord (one key per step) to a command. This is the canonical entry point.</summary>
    void Bind(IReadOnlyList<Key> chord, string commandId);

    /// <summary>Remove the binding at <paramref name="chord"/>. Returns true if a binding was removed.</summary>
    bool Unbind(IReadOnlyList<Key> chord);

    /// <summary>
    /// Report whether <paramref name="chord"/> would conflict with any existing binding, or null
    /// if it would not. Use before <see cref="Bind(IReadOnlyList{Key}, string)"/> to decide whether
    /// to surface a confirm/refuse dialog.
    /// </summary>
    KeybindingConflict? CheckConflict(IReadOnlyList<Key> chord);

    /// <summary>
    /// String sugar for the hardcoded defaults: parses a space-separated sequence
    /// ("Ctrl+S", "Ctrl+W X") and binds it. Throws on an unparseable sequence — callers binding
    /// user-supplied input should use the <see cref="Key"/>-list overload, whose identity never
    /// round-trips through the lossy display parser (issue #89).
    /// </summary>
    void Bind(string keySequence, string commandId);

    /// <summary>String sugar for <see cref="Unbind(IReadOnlyList{Key})"/>; see <see cref="Bind(string, string)"/>.</summary>
    bool Unbind(string keySequence);

    /// <summary>Clear all bindings.</summary>
    void Reset();

    /// <summary>String sugar for <see cref="CheckConflict(IReadOnlyList{Key})"/>; see <see cref="Bind(string, string)"/>.</summary>
    KeybindingConflict? CheckConflict(string keySequence);

    /// <summary>All currently-registered bindings as (chord, commandId) pairs.</summary>
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
