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

public interface IKeybindingService
{
    /// <summary>
    /// Bind a key sequence to a command. Sequences are space-separated, e.g.
    /// "Ctrl+S" for a single key, "Ctrl+W X" for a chord.
    /// </summary>
    void Bind(string keySequence, string commandId);

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
