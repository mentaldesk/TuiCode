using Terminal.Gui.Time;
using TuiCode.Abstractions;

namespace TuiCode.Workbench;

public sealed class WorkbenchHost : IDisposable
{
    private readonly TerminalFlowControl _flowControl;
    private readonly IApplication _app;
    private readonly Workbench _workbench;
    private readonly ICommandService _commands;
    private readonly IKeybindingService _keybindings;
    private bool _disposed;

    public WorkbenchHost(
        Workbench workbench,
        ICommandService commands,
        IKeybindingService keybindings,
        ITimeProvider? timeProvider = null)
    {
        // Neutralize TG's default Esc-as-Quit by reassigning the built-in
        // Quit command to a key we never bind in our own service. Our Ctrl+Q
        // binding fires through the IKeybindingService instead.
        NeutralizeBuiltinQuitKey();

        _flowControl = new TerminalFlowControl();
        _app = Application.Create(timeProvider ?? new SystemTimeProvider());
        _app.Init(driverName: null!);
        _workbench = workbench;
        _commands = commands;
        _keybindings = keybindings;

        RegisterDefaultCommands();
        RegisterDefaultKeybindings();

        _app.Keyboard.KeyDown += OnAppKeyDown;
        _keybindings.ChordChanged += OnChordChanged;
    }

    public IApplication App => _app;
    public Workbench Workbench => _workbench;

    public void Run() => _app.Run(_workbench, errorHandler: null!);

    public Task<object?> RunAsync(CancellationToken ct = default) =>
        _app.RunAsync(_workbench, ct, errorHandler: null!);

    private void OnAppKeyDown(object? sender, Key key)
    {
        var result = _keybindings.Handle(key);
        if (result != KeyHandlingResult.Pass)
            key.Handled = true;
    }

    private void OnChordChanged(object? sender, string? chord) =>
        _workbench.StatusBar.SetChord(chord);

    private void RegisterDefaultCommands()
    {
        _commands.Register(CommandIds.Quit, () => _app.RequestStop());
        _commands.Register(CommandIds.SaveActiveEditor, () => _workbench.Editor.Save());
        _commands.Register(CommandIds.FocusExplorer, () => _workbench.Sidebar.Explorer.SetFocus());
        _commands.Register(CommandIds.FocusEditor, () => _workbench.Editor.SetFocus());
        _commands.Register(CommandIds.FocusNextPart, FocusNextPart);
        _commands.Register(CommandIds.FocusPreviousPart, FocusNextPart);
    }

    private void FocusNextPart()
    {
        // Two focusable parts for now; same cycle either direction.
        if (_workbench.Sidebar.Explorer.HasFocus)
            _workbench.Editor.SetFocus();
        else
            _workbench.Sidebar.Explorer.SetFocus();
    }

    private void RegisterDefaultKeybindings()
    {
        _keybindings.Bind("Ctrl+Q", CommandIds.Quit);
        _keybindings.Bind("Ctrl+S", CommandIds.SaveActiveEditor);
        _keybindings.Bind("F6", CommandIds.FocusNextPart);
        _keybindings.Bind("Shift+F6", CommandIds.FocusPreviousPart);
        _keybindings.Bind("Ctrl+W X", CommandIds.FocusExplorer);
        _keybindings.Bind("Ctrl+W E", CommandIds.FocusEditor);
    }

    private static void NeutralizeBuiltinQuitKey()
    {
        // Reassign TG's default Quit binding away from Esc onto Ctrl+Q.
        // Our IKeybindingService also binds Ctrl+Q to CommandIds.Quit and
        // wins via the app-level KeyDown intercept (Handled = true), but
        // TG's binding stays in place as a fallback.
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
        _app.Keyboard.KeyDown -= OnAppKeyDown;
        _keybindings.ChordChanged -= OnChordChanged;
        _workbench.Dispose();
        _app.Dispose();
        _flowControl.Dispose();
    }
}
