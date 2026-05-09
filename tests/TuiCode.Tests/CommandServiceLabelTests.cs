using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class CommandServiceLabelTests
{
    [Fact]
    public void Register_without_a_label_uses_the_id_as_the_label()
    {
        var svc = new CommandService();
        svc.Register("foo.bar", () => { });

        var entry = Assert.Single(svc.Registered);
        Assert.Equal("foo.bar", entry.Id);
        Assert.Equal("foo.bar", entry.Label);
    }

    [Fact]
    public void Register_with_a_label_exposes_it_via_Registered()
    {
        var svc = new CommandService();
        svc.Register("foo.bar", "Frobulate the bars", () => { });

        var entry = Assert.Single(svc.Registered);
        Assert.Equal("Frobulate the bars", entry.Label);
    }

    [Fact]
    public void Register_replaces_a_prior_registration_of_the_same_id()
    {
        var svc = new CommandService();
        var calls = new List<string>();
        svc.Register("foo", "First", () => calls.Add("first"));
        svc.Register("foo", "Second", () => calls.Add("second"));

        svc.TryExecute("foo");

        Assert.Equal(new[] { "second" }, calls);
        Assert.Equal("Second", svc.Registered.Single().Label);
    }
}
