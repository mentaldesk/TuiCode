namespace TuiCode.Abstractions;

public interface ICommandService
{
    void Register(string commandId, Action handler);
    bool TryExecute(string commandId);
    bool IsRegistered(string commandId);
}
