namespace TuiCode.Abstractions;

/// <summary>
/// Where a command's keybindings fire. Fixed at registration; the user can't change it. A key is looked up
/// in the focused scope first, then in <see cref="Global"/>.
/// </summary>
public enum CommandScope
{
    Global,
    Editor,
    Explorer,
    Find,
}
