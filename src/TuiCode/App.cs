using Microsoft.Extensions.DependencyInjection;
using TuiCode.Workbench;

public sealed class App
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services) => _services = services;

    public void Run()
    {
        using var host = _services.GetRequiredService<WorkbenchHost>();
        host.Run();
    }
}
