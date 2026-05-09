using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class InputScopeStackTests
{
    [Fact]
    public void Handle_returns_Pass_when_stack_is_empty()
    {
        var stack = new InputScopeStack();
        var key = new Key('a');

        Assert.Equal(KeyHandlingResult.Pass, stack.Handle(key));
    }

    [Fact]
    public void Handle_delegates_to_top_of_stack()
    {
        var commands = new CommandService();
        var bottom = new KeybindingService(commands);
        var top = new KeybindingService(commands);
        var fired = "";
        commands.Register("bottom.action", () => fired = "bottom");
        commands.Register("top.action", () => fired = "top");
        bottom.Bind("Ctrl+B", "bottom.action");
        top.Bind("Ctrl+B", "top.action");

        var stack = new InputScopeStack();
        stack.Push(bottom);
        stack.Push(top);

        Key.TryParse("Ctrl+B", out var ctrlB);
        Assert.Equal(KeyHandlingResult.Consumed, stack.Handle(ctrlB));
        Assert.Equal("top", fired);
    }

    [Fact]
    public void Pop_restores_the_previous_top()
    {
        var commands = new CommandService();
        var bottom = new KeybindingService(commands);
        var top = new KeybindingService(commands);
        var fired = "";
        commands.Register("bottom.action", () => fired = "bottom");
        commands.Register("top.action", () => fired = "top");
        bottom.Bind("Ctrl+B", "bottom.action");
        top.Bind("Ctrl+B", "top.action");

        var stack = new InputScopeStack();
        stack.Push(bottom);
        stack.Push(top);
        stack.Pop(top);

        Key.TryParse("Ctrl+B", out var ctrlB);
        stack.Handle(ctrlB);
        Assert.Equal("bottom", fired);
    }

    [Fact]
    public void Pop_throws_when_argument_is_not_the_current_top()
    {
        var stack = new InputScopeStack();
        var commands = new CommandService();
        var a = new KeybindingService(commands);
        var b = new KeybindingService(commands);
        stack.Push(a);
        stack.Push(b);

        Assert.Throws<InvalidOperationException>(() => stack.Pop(a));
    }

    [Fact]
    public void Pop_throws_when_stack_is_empty()
    {
        var stack = new InputScopeStack();
        var commands = new CommandService();
        var s = new KeybindingService(commands);

        Assert.Throws<InvalidOperationException>(() => stack.Pop(s));
    }
}
