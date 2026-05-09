namespace TuiCode.Abstractions;

/// <summary>
/// Stack of <see cref="IKeybindingService"/> scopes. The top-of-stack scope receives
/// keys; lower frames are dormant until popped. Modal UIs (settings, dialogs, command
/// palette) push their own scope on open and pop on close, so workbench shortcuts
/// don't fire while a modal is active.
/// </summary>
public interface IInputScopeStack
{
    /// <summary>Push a scope. It becomes the active handler for subsequent keys.</summary>
    void Push(IKeybindingService scope);

    /// <summary>
    /// Pop the top scope. The argument must equal the current top — passing a stale
    /// reference throws, which catches order-of-pop bugs early.
    /// </summary>
    void Pop(IKeybindingService scope);

    /// <summary>Delegate handling to the top-of-stack scope.</summary>
    KeyHandlingResult Handle(Key key);
}
