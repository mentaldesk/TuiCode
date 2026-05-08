using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class CommandServiceTests
{
    [Fact]
    public void TryExecute_runs_a_registered_handler_and_returns_true()
    {
        var service = new CommandService();
        var fired = false;
        service.Register("test", () => fired = true);

        var ran = service.TryExecute("test");

        Assert.True(ran);
        Assert.True(fired);
    }

    [Fact]
    public void TryExecute_returns_false_for_unregistered_commands()
    {
        var service = new CommandService();
        Assert.False(service.TryExecute("missing"));
    }

    [Fact]
    public void Register_replaces_an_existing_handler()
    {
        var service = new CommandService();
        var first = 0;
        var second = 0;
        service.Register("test", () => first++);
        service.Register("test", () => second++);

        service.TryExecute("test");

        Assert.Equal(0, first);
        Assert.Equal(1, second);
    }
}
