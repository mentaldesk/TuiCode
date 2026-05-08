using Terminal.Gui.Time;

namespace TuiCode.Workbench;

public sealed class WorkbenchHost : IDisposable
{
    private readonly TerminalFlowControl _flowControl;
    private readonly IApplication _app;
    private readonly Workbench _workbench;
    private bool _disposed;

    public WorkbenchHost(Workbench workbench, ITimeProvider? timeProvider = null)
    {
        ConfigureDefaultKeyBindings();
        _flowControl = new TerminalFlowControl();
        _app = Application.Create(timeProvider ?? new SystemTimeProvider());
        _app.Init(driverName: null!);
        _workbench = workbench;
    }

    public IApplication App => _app;
    public Workbench Workbench => _workbench;

    public void Run() => _app.Run(_workbench, errorHandler: null!);

    public Task<object?> RunAsync(CancellationToken ct = default) =>
        _app.RunAsync(_workbench, ct, errorHandler: null!);

    private static void ConfigureDefaultKeyBindings()
    {
        if (Key.TryParse("Ctrl+Q", out var ctrlQ))
        {
            Application.SetDefaultKeyBinding(
                Command.Quit,
                new PlatformKeyBinding { All = [ctrlQ] });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workbench.Dispose();
        _app.Dispose();
        _flowControl.Dispose();
    }
}
