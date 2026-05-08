using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

public sealed class CommandService : ICommandService
{
    private readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);

    public void Register(string commandId, Action handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(commandId);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[commandId] = handler;
    }

    public bool TryExecute(string commandId)
    {
        if (!_handlers.TryGetValue(commandId, out var handler))
            return false;
        handler();
        return true;
    }

    public bool IsRegistered(string commandId) => _handlers.ContainsKey(commandId);
}
