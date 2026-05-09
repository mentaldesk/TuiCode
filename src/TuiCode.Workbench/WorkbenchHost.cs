using Terminal.Gui.Time;
using TuiCode.Abstractions;
using TuiCode.Workbench.Settings;

namespace TuiCode.Workbench;

public sealed class WorkbenchHost : IDisposable
{
    private const int MaxIndexedEditorBindings = 9;

    private readonly TerminalFlowControl _flowControl;
    private readonly IApplication _app;
    private readonly Workbench _workbench;
    private readonly ICommandService _commands;
    private readonly IKeybindingService _keybindings;
    private readonly IInputScopeStack _scopes;
    private readonly ISettingsService _settings;
    private FocusLevel _focusLevel = FocusLevel.EditorBody;
    private SettingsView? _activeSettings;
    private bool _disposed;

    public WorkbenchHost(
        Workbench workbench,
        ICommandService commands,
        IKeybindingService keybindings,
        IInputScopeStack scopes,
        ISettingsService settings,
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
        _scopes = scopes;
        _settings = settings;

        RegisterDefaultCommands();
        RegisterDefaultKeybindings();

        // Workbench scope is the bottom of the input stack; never popped.
        _scopes.Push(_keybindings);

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
        var result = _scopes.Handle(key);
        if (result != KeyHandlingResult.Pass)
        {
            key.Handled = true;
            return;
        }

        // When the tab strip is the logical focus, the active TextView still
        // has TG focus underneath. Intercept tab-navigation keys before they
        // reach the editor. (Only relevant in the workbench scope; modals
        // don't reach this path because they consume keys above.)
        if (_activeSettings is null
            && _focusLevel == FocusLevel.EditorTabStrip
            && TryHandleTabStripKey(key))
            key.Handled = true;
    }

    private bool TryHandleTabStripKey(Key key)
    {
        if (key == Key.CursorLeft) { _commands.TryExecute(CommandIds.PreviousEditor); return true; }
        if (key == Key.CursorRight) { _commands.TryExecute(CommandIds.NextEditor); return true; }
        if (key == Key.CursorUp) return true; // explicit no-op so the textview doesn't move the cursor
        if (key == Key.CursorDown || key == Key.Enter)
        {
            _commands.TryExecute(CommandIds.FocusEditorBody);
            return true;
        }
        return false;
    }

    private void OnChordChanged(object? sender, string? chord) =>
        _workbench.StatusBar.SetChord(chord);

    private void RegisterDefaultCommands()
    {
        _commands.Register(CommandIds.Quit, () => _app.RequestStop());
        _commands.Register(CommandIds.SaveActiveEditor, () => _workbench.Editor.Save());
        _commands.Register(CommandIds.CloseActiveEditor, () => _workbench.Editor.CloseActive());
        _commands.Register(CommandIds.NextEditor, () => _workbench.Editor.NextTab());
        _commands.Register(CommandIds.PreviousEditor, () => _workbench.Editor.PreviousTab());

        _commands.Register(CommandIds.ToggleSidebar, ToggleSidebar);
        _commands.Register(CommandIds.FocusEditorBody, FocusEditorBody);
        _commands.Register(CommandIds.FocusEditorTabStrip, FocusEditorTabStrip);
        _commands.Register(CommandIds.OpenSettings, OpenSettings);

        for (var i = 1; i <= MaxIndexedEditorBindings; i++)
        {
            var index = i;
            _commands.Register(CommandIds.FocusEditorByIndex(index), () => FocusEditorAt(index - 1));
        }
    }

    private void RegisterDefaultKeybindings()
    {
        _keybindings.Bind("Ctrl+Q", CommandIds.Quit);
        _keybindings.Bind("Ctrl+S", CommandIds.SaveActiveEditor);
        _keybindings.Bind("Ctrl+W", CommandIds.CloseActiveEditor);
        _keybindings.Bind("Ctrl+Tab", CommandIds.NextEditor);
        _keybindings.Bind("Ctrl+Shift+Tab", CommandIds.PreviousEditor);

        _keybindings.Bind("Ctrl+D0", CommandIds.ToggleSidebar);
        _keybindings.Bind("Esc", CommandIds.FocusEditorBody);
        _keybindings.Bind("Ctrl+Esc", CommandIds.FocusEditorTabStrip);
        _keybindings.Bind("Ctrl+,", CommandIds.OpenSettings);

        for (var i = 1; i <= MaxIndexedEditorBindings; i++)
            _keybindings.Bind($"Ctrl+D{i}", CommandIds.FocusEditorByIndex(i));
    }

    private void ToggleSidebar()
    {
        if (!_workbench.IsSidebarVisible)
        {
            _workbench.SetSidebarVisible(true);
            _workbench.Sidebar.Explorer.SetFocus();
            _focusLevel = FocusLevel.Sidebar;
            return;
        }

        if (_workbench.Sidebar.Explorer.HasFocus || _focusLevel == FocusLevel.Sidebar)
        {
            _workbench.SetSidebarVisible(false);
            FocusEditorBody();
            return;
        }

        _workbench.Sidebar.Explorer.SetFocus();
        _focusLevel = FocusLevel.Sidebar;
    }

    private void FocusEditorBody()
    {
        if (_workbench.Editor.Group.ActiveTab is { } tab)
            tab.FocusContent();
        _focusLevel = FocusLevel.EditorBody;
    }

    private void FocusEditorTabStrip()
    {
        // Only meaningful from the editor body; from anywhere else it's a no-op.
        if (_focusLevel != FocusLevel.EditorBody) return;
        if (_workbench.Editor.Group.ActiveTab is null) return;
        _focusLevel = FocusLevel.EditorTabStrip;
    }

    private void FocusEditorAt(int zeroBasedIndex)
    {
        if (!_workbench.Editor.Group.FocusByIndex(zeroBasedIndex)) return;
        FocusEditorBody();
    }

    private void OpenSettings()
    {
        if (_activeSettings is not null) return;

        var view = new SettingsView(_settings);
        view.Closed += (_, _) => CloseSettings(view);
        _activeSettings = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.SetFocus();
    }

    private void CloseSettings(SettingsView view)
    {
        if (!ReferenceEquals(_activeSettings, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeSettings = null;
        FocusEditorBody();
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

    private enum FocusLevel { Sidebar, EditorTabStrip, EditorBody }
}
