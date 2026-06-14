namespace TuiCode.Abstractions;

/// <summary>
/// One entry in the user's keybinding override list: a chord plus the command it maps to.
/// The chord is the canonical identity (raw <see cref="KeyCode"/> per step, issue #89), not a
/// display string. <see cref="Command"/> may be prefixed with <c>-</c> to remove an existing
/// binding (e.g. <c>"-workbench.action.openSettings"</c>). Order is meaningful: later entries
/// can shadow earlier ones.
/// </summary>
public sealed class KeybindingOverride : IEquatable<KeybindingOverride>
{
    public KeybindingOverride(IReadOnlyList<Key> keys, string command)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) throw new ArgumentException("A chord needs at least one key.", nameof(keys));
        // Command is intentionally unvalidated here: a hand-edited file can carry an empty/garbage
        // command, and the override list must still load so the *rest* of the file applies. The bad
        // entry is caught and logged when WorkbenchHost.ApplyKeybindings tries to Bind it (#90).
        ArgumentNullException.ThrowIfNull(command);
        Keys = keys;
        Command = command;
    }

    public IReadOnlyList<Key> Keys { get; }
    public string Command { get; }

    public bool IsRemoval => Command.Length > 0 && Command[0] == '-';
    public string EffectiveCommand => IsRemoval ? Command[1..] : Command;

    /// <summary>Parse-stable identity (keycode-based); UI/display lives in <see cref="KeyChord.Display"/>.</summary>
    public string CanonicalId => KeyChord.Canonical(Keys);

    public bool Equals(KeybindingOverride? other) =>
        other is not null
        && string.Equals(Command, other.Command, StringComparison.Ordinal)
        && string.Equals(CanonicalId, other.CanonicalId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as KeybindingOverride);

    public override int GetHashCode() => HashCode.Combine(CanonicalId, Command);
}
