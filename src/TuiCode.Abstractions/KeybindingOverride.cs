namespace TuiCode.Abstractions;

/// <summary>
/// One entry in the user's keybinding override list. <see cref="Command"/> may be prefixed
/// with <c>-</c> to remove an existing binding (e.g. <c>"-workbench.action.openSettings"</c>).
/// Order is meaningful: later entries can shadow earlier ones.
/// </summary>
public sealed record KeybindingOverride(string Key, string Command)
{
    public bool IsRemoval => Command.Length > 0 && Command[0] == '-';
    public string EffectiveCommand => IsRemoval ? Command[1..] : Command;

    /// <summary>Serialize as <c>"Key|Command"</c> for storage in TG's config.</summary>
    public string Format() => $"{Key}|{Command}";

    /// <summary>Parse a <c>"Key|Command"</c> string. Returns null for malformed entries.</summary>
    public static KeybindingOverride? Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var pipe = raw.IndexOf('|');
        if (pipe <= 0 || pipe == raw.Length - 1) return null;
        return new KeybindingOverride(raw[..pipe], raw[(pipe + 1)..]);
    }
}
