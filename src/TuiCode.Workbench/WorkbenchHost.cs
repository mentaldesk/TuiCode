using Terminal.Gui.Time;
using TuiCode.Abstractions;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Services;
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
    private ActionView? _activeActions;
    private HelpView? _activeHelp;
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
        ApplyKeybindings(_settings.KeybindingOverrides);

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
        _commands.Register(CommandIds.Quit, "Quit", () => _app.RequestStop());
        _commands.Register(CommandIds.SaveActiveEditor, "Save active editor", () => _workbench.Editor.Save());
        _commands.Register(CommandIds.CloseActiveEditor, "Close active editor", () => _workbench.Editor.CloseActive());
        _commands.Register(CommandIds.NextEditor, "Next editor", () => _workbench.Editor.NextTab());
        _commands.Register(CommandIds.PreviousEditor, "Previous editor", () => _workbench.Editor.PreviousTab());

        _commands.Register(CommandIds.ToggleSidebar, "Toggle sidebar", ToggleSidebar);
        _commands.Register(CommandIds.FocusEditorBody, "Focus editor", FocusEditorBody);
        _commands.Register(CommandIds.FocusEditorTabStrip, "Focus editor tab strip", FocusEditorTabStrip);
        _commands.Register(CommandIds.OpenSettings, "Open settings", OpenSettings);
        _commands.Register(CommandIds.ShowActions, "Show all commands", OpenActions);
        _commands.Register(CommandIds.ShowHelp, "Getting Started (help)", OpenHelp);

        for (var i = 1; i <= MaxIndexedEditorBindings; i++)
        {
            var index = i;
            _commands.Register(CommandIds.FocusEditorByIndex(index), $"Focus editor tab {index}", () => FocusEditorAt(index - 1));
        }
    }

    /// <summary>
    /// Replace the entire workbench-scope binding set: clear, re-apply defaults, then layer on
    /// the user's overrides. Called at startup and again when the settings UI commits a change.
    /// </summary>
    public void ApplyKeybindings(IEnumerable<KeybindingOverride> overrides)
    {
        _keybindings.Reset();
        BindDefaults(_keybindings);
        foreach (var o in overrides)
        {
            if (o.IsRemoval) _keybindings.Unbind(o.Key);
            else _keybindings.Bind(o.Key, o.EffectiveCommand);
        }
    }

    /// <summary>
    /// Take the picker's edited binding set, compute the diff against defaults, persist as the
    /// new override list, and apply it to the live keybinding service.
    /// </summary>
    public void ApplyEditedBindings(IEnumerable<KeyBinding> editedBindings)
    {
        var defaults = GetDefaultBindings().ToDictionary(b => b.Sequence, b => b.CommandId, StringComparer.Ordinal);
        var edited = editedBindings.ToDictionary(b => b.Sequence, b => b.CommandId, StringComparer.Ordinal);

        var overrides = new List<KeybindingOverride>();
        foreach (var seq in defaults.Keys.Union(edited.Keys, StringComparer.Ordinal))
        {
            var hasDefault = defaults.TryGetValue(seq, out var defaultCmd);
            var hasEdited = edited.TryGetValue(seq, out var editedCmd);

            if (hasDefault && !hasEdited)
                overrides.Add(new KeybindingOverride(seq, "-" + defaultCmd));
            else if (hasEdited && (!hasDefault || !string.Equals(defaultCmd, editedCmd, StringComparison.Ordinal)))
                overrides.Add(new KeybindingOverride(seq, editedCmd!));
        }

        _settings.SetKeybindingOverrides(overrides);
        ApplyKeybindings(overrides);
    }

    /// <summary>
    /// The workbench's hard-coded default bindings, materialised against a throwaway service
    /// so the picker can compute diffs without touching the live trie.
    /// </summary>
    public IEnumerable<KeyBinding> GetDefaultBindings()
    {
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        BindDefaults(keybindings);
        return keybindings.Bindings.ToArray();
    }

    private static void BindDefaults(IKeybindingService keybindings)
    {
        keybindings.Bind("Ctrl+Q", CommandIds.Quit);
        keybindings.Bind("Ctrl+S", CommandIds.SaveActiveEditor);
        keybindings.Bind("Ctrl+W", CommandIds.CloseActiveEditor);
        keybindings.Bind("Ctrl+Tab", CommandIds.NextEditor);
        keybindings.Bind("Ctrl+Shift+Tab", CommandIds.PreviousEditor);

        keybindings.Bind("Ctrl+D0", CommandIds.ToggleSidebar);
        keybindings.Bind("Esc", CommandIds.FocusEditorBody);
        keybindings.Bind("Ctrl+Esc", CommandIds.FocusEditorTabStrip);
        keybindings.Bind("Ctrl+,", CommandIds.OpenSettings);
        keybindings.Bind("F1", CommandIds.ShowActions);
        keybindings.Bind("Ctrl+?", CommandIds.ShowHelp);

        for (var i = 1; i <= MaxIndexedEditorBindings; i++)
            keybindings.Bind($"Ctrl+D{i}", CommandIds.FocusEditorByIndex(i));
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

        var view = new SettingsView(_settings, _keybindings, _commands, _scopes, ApplyEditedBindings);
        view.Closed += (_, _) => CloseSettings(view);
        _activeSettings = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        // Focus categories explicitly post-mount; pre-mount SetFocus calls in the SettingsView
        // constructor are no-ops because the view isn't yet in the focus tree.
        view.FocusCategories();
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

    private void OpenActions()
    {
        if (_activeActions is not null) return;

        var view = new ActionView(_commands, _keybindings, commandId => _commands.TryExecute(commandId));
        view.Closed += (_, _) => CloseActions(view);
        _activeActions = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusSearch();
    }

    private void CloseActions(ActionView view)
    {
        if (!ReferenceEquals(_activeActions, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeActions = null;
        FocusEditorBody();
    }

    private void OpenHelp()
    {
        if (_activeHelp is not null) return;

        var view = new HelpView();
        view.Closed += (_, _) => CloseHelp(view);
        _activeHelp = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.SetFocus();
    }

    private void CloseHelp(HelpView view)
    {
        if (!ReferenceEquals(_activeHelp, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeHelp = null;
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
