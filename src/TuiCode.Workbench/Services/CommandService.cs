using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

public sealed class CommandService : ICommandService
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public void Register(string commandId, Action handler) =>
        Register(commandId, commandId, handler);

    public void Register(string commandId, string label, Action handler, CommandScope scope = CommandScope.Global, Func<bool>? isEnabled = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(commandId);
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentNullException.ThrowIfNull(handler);
        _entries[commandId] = new Entry(label, handler, scope, isEnabled);
    }

    public bool TryExecute(string commandId)
    {
        if (!_entries.TryGetValue(commandId, out var entry))
            return false;
        entry.Handler();
        return true;
    }

    public bool IsRegistered(string commandId) => _entries.ContainsKey(commandId);

    public CommandScope ScopeOf(string commandId) =>
        _entries.TryGetValue(commandId, out var entry) ? entry.Scope : CommandScope.Global;

    public bool IsEnabled(string commandId) =>
        !_entries.TryGetValue(commandId, out var entry) || entry.IsEnabled?.Invoke() != false;

    public IEnumerable<CommandDescriptor> Registered =>
        _entries.Select(kv => new CommandDescriptor(kv.Key, kv.Value.Label, kv.Value.Scope));

    private sealed record Entry(string Label, Action Handler, CommandScope Scope, Func<bool>? IsEnabled);
}
