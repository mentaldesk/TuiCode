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
}
