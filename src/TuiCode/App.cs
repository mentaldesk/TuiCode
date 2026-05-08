using TuiCode.Workbench;

public sealed class App : IDisposable
{
    public WorkbenchHost Host { get; }

    public App(WorkbenchHost host) => Host = host;

    public void Run() => Host.Run();

    public void Dispose() => Host.Dispose();
}
