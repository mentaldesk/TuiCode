using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

public sealed class InputScopeStack : IInputScopeStack
{
    private readonly Stack<IKeybindingService> _stack = new();

    public void Push(IKeybindingService scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _stack.Push(scope);
    }

    public void Pop(IKeybindingService scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (_stack.Count == 0)
            throw new InvalidOperationException("Pop called on an empty input scope stack.");
        if (!ReferenceEquals(_stack.Peek(), scope))
            throw new InvalidOperationException(
                "Pop called with a scope that is not the current top. Pops must be in reverse order of pushes.");
        _stack.Pop();
    }

    public KeyHandlingResult Handle(Key key) =>
        _stack.Count == 0 ? KeyHandlingResult.Pass : _stack.Peek().Handle(key);
}
