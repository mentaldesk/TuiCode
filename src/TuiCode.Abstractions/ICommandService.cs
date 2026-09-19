namespace TuiCode.Abstractions;

public sealed record CommandDescriptor(string Id, string Label, CommandScope Scope = CommandScope.Global);

public interface ICommandService
{
    /// <summary>Register a command with no human-readable label (label defaults to the id).</summary>
    void Register(string commandId, Action handler);

    /// <summary>
    /// Register a command with a human-readable label used by the keybindings picker
    /// and the help dialog. Replaces any prior registration of the same id. Its keybindings only fire in
    /// <paramref name="scope"/>; while <paramref name="isEnabled"/> says no, its keys fall through to Global.
    /// </summary>
    void Register(string commandId, string label, Action handler, CommandScope scope = CommandScope.Global, Func<bool>? isEnabled = null);

    bool TryExecute(string commandId);
    bool IsRegistered(string commandId);

    /// <summary>The scope <paramref name="commandId"/> was registered with; Global if it isn't registered.</summary>
    CommandScope ScopeOf(string commandId);

    /// <summary>Whether <paramref name="commandId"/>'s keys should fire right now. True if it isn't registered.</summary>
    bool IsEnabled(string commandId);

    /// <summary>All currently-registered commands as (id, label) pairs.</summary>
    IEnumerable<CommandDescriptor> Registered { get; }
}
