namespace TuiCode.Abstractions;

public sealed record CommandDescriptor(string Id, string Label);

public interface ICommandService
{
    /// <summary>Register a command with no human-readable label (label defaults to the id).</summary>
    void Register(string commandId, Action handler);

    /// <summary>
    /// Register a command with a human-readable label used by the keybindings picker
    /// and the help dialog. Replaces any prior registration of the same id.
    /// </summary>
    void Register(string commandId, string label, Action handler);

    bool TryExecute(string commandId);
    bool IsRegistered(string commandId);

    /// <summary>All currently-registered commands as (id, label) pairs.</summary>
    IEnumerable<CommandDescriptor> Registered { get; }
}
