using Terminal.Gui.Drivers;
using Terminal.Gui.Time;
using TuiCode.Abstractions;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Diagnostics;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Mnemonics;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Workbench;

public sealed class WorkbenchHost : IDisposable
{
    private const int MaxIndexedEditorBindings = 9;

    // On Windows the Win32 console reports Ctrl+Enter as Ctrl + LineFeed (0x0A) instead of
    // Ctrl + Enter (CR, 0x0D): plain Enter yields CR, but holding Ctrl swaps the produced char
    // to LF, and TG's native WindowsDriver passes that control char straight through as the
    // keycode. The result (0x4000000A) never matches a "Ctrl+Enter" binding (0x4000000D), so the
    // chord is dead on Windows. We rewrite it back to Ctrl+Enter for binding lookup. Windows-only:
    // the ansi/kitty path elsewhere already delivers Ctrl+Enter as CR. (See AGENTS.md key handling.)
    private const KeyCode WindowsCtrlLineFeed = KeyCode.CtrlMask | (KeyCode)0x0A;
    private const KeyCode CtrlEnter = KeyCode.CtrlMask | KeyCode.Enter;

    private readonly TerminalFlowControl _flowControl;
    private readonly IApplication _app;
    private readonly Workbench _workbench;
    private readonly ICommandService _commands;
    private readonly IKeybindingService _keybindings;
    private readonly IInputScopeStack _scopes;
    private readonly ISettingsService _settings;
    private readonly IReadOnlyList<ITerminalIntegration> _terminalIntegrations;
    private readonly IEnvironment _environment;
    private FocusLevel _focusLevel = FocusLevel.EditorBody;
    private readonly CursorLocationHistory _history = new();
    // Set while we drive the cursor ourselves (Back/Forward, Go-to-line) so those moves
    // don't get re-recorded as fresh jumps.
    private bool _suppressHistory;
    private SettingsView? _activeSettings;
    private ActionView? _activeActions;
    private HelpView? _activeHelp;
    private GoToLineView? _activeGoToLine;
    private DiagnosticsView? _activeDiagnostics;
    private MnemonicView? _activeMnemonics;
    private OpenView? _activeOpen;
    private NewPathView? _activeNewPath;
    private bool _disposed;

    public WorkbenchHost(
        Workbench workbench,
        ICommandService commands,
        IKeybindingService keybindings,
        IInputScopeStack scopes,
        ISettingsService settings,
        IEnumerable<ITerminalIntegration>? terminalIntegrations = null,
        IEnvironment? environment = null,
        ITimeProvider? timeProvider = null,
        string? driverName = null)
    {
        // Neutralize TG's default Esc-as-Quit by reassigning the built-in
        // Quit command to a key we never bind in our own service. Our Ctrl+Q
        // binding fires through the IKeybindingService instead.
        NeutralizeBuiltinQuitKey();

        _flowControl = new TerminalFlowControl();
        _app = Application.Create(timeProvider ?? new SystemTimeProvider());
        // Production passes null so TG auto-selects the best platform driver. Tests pass
        // DriverRegistry.Names.ANSI to force the headless ANSI driver: the real WindowsDriver
        // blocks on console input when no console is attached, hanging headless CI/test runs
        // (and RunAsync never observes the cancellation token while blocked there).
        _app.Init(driverName: driverName!);

        // OSC 1337 SetUserVar TUICODE_ACTIVE=1 (base64 "MQ=="). WezTerm's tuicode.lua keys off
        // this user-var to activate its key table only while TuiCode runs; other terminals
        // strip the unknown OSC silently. Unconditional — no detection needed.
        Console.Out.Write("\x1b]1337;SetUserVar=TUICODE_ACTIVE=MQ==\x07");
        Console.Out.Flush();
        _workbench = workbench;
        _commands = commands;
        _keybindings = keybindings;
        _scopes = scopes;
        _settings = settings;
        _terminalIntegrations = (terminalIntegrations ?? Array.Empty<ITerminalIntegration>()).ToArray();
        _environment = environment ?? new SystemEnvironment();

        RegisterDefaultCommands();
        ApplyKeybindings(_settings.KeybindingOverrides);

        // Workbench scope is the bottom of the input stack; never popped.
        _scopes.Push(_keybindings);

        _app.Keyboard.KeyDown += OnAppKeyDown;
        _keybindings.ChordChanged += OnChordChanged;

        // Feed the cursor-location history (#35): within-file moves come from CursorMoved,
        // file switches (manual tab cycling, opening a file) from ActiveTabChanged. The
        // history's own heuristic decides which of these count as navigable jumps.
        _workbench.Editor.Group.CursorMoved += OnEditorCursorMoved;
        _workbench.Editor.Group.ActiveTabChanged += OnActiveTabChanged;
    }

    public IApplication App => _app;
    public Workbench Workbench => _workbench;

    public void Run() => _app.Run(_workbench, errorHandler: null!);

    public Task<object?> RunAsync(CancellationToken ct = default) =>
        _app.RunAsync(_workbench, ct, errorHandler: null!);

    private void OnAppKeyDown(object? sender, Key key)
    {
        // Diagnostics shows the raw key as delivered by the driver, so feed it the unnormalized key.
        _activeDiagnostics?.UpdateLastKey(key);

        // Look the binding up under the normalized chord, but consume the original event object so
        // the Win32 LF (0x0A) never falls through to the editor when a binding claimed it.
        var result = _scopes.Handle(NormalizeWindowsCtrlEnter(key));
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

    private Key NormalizeWindowsCtrlEnter(Key key) =>
        _environment.IsWindows && key.KeyCode == WindowsCtrlLineFeed ? new Key(CtrlEnter) : key;

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
        _commands.Register(CommandIds.FocusSidebar, "Focus sidebar", FocusExplorer);
        _commands.Register(CommandIds.FocusEditorBody, "Focus editor", FocusEditorBody);
        _commands.Register(CommandIds.FocusEditorTabStrip, "Focus editor tab strip", FocusEditorTabStrip);
        _commands.Register(CommandIds.OpenSettings, "Open settings", OpenSettings);
        _commands.Register(CommandIds.Open, "Open file or folder", OpenFileOrFolder);
        _commands.Register(CommandIds.New, "New file or folder", OpenNewPath);
        _commands.Register(CommandIds.ShowActions, "Show all commands", OpenActions);
        _commands.Register(CommandIds.ShowMnemonics, "Show mnemonics", OpenMnemonics);
        _commands.Register(CommandIds.ShowHelp, "Getting Started (help)", OpenHelp);
        _commands.Register(CommandIds.GoToLine, "Go to line:column", OpenGoToLine);
        _commands.Register(CommandIds.NavigateBack, "Previous cursor position", NavigateBack);
        _commands.Register(CommandIds.NavigateForward, "Next cursor position", NavigateForward);
        _commands.Register(CommandIds.ShowDiagnostics, "Show diagnostics", OpenDiagnostics);

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

        // No default key for ToggleSidebar — Ctrl+0 is eaten by the terminal's own zoom-reset
        // in many emulators (#81), so it was unreliable. Reach it via the `ts` mnemonic instead.
        keybindings.Bind("Esc", CommandIds.FocusEditorBody);
        keybindings.Bind("Ctrl+Esc", CommandIds.FocusEditorTabStrip);
        keybindings.Bind("Ctrl+,", CommandIds.OpenSettings);
        keybindings.Bind("Ctrl+O", CommandIds.Open);
        keybindings.Bind("Ctrl+N", CommandIds.New);
        keybindings.Bind("Ctrl+E", CommandIds.ShowActions);
        // Leader key for the mnemonic launcher (issue #50). Rebindable like any shortcut;
        // the mnemonics it dispatches are fixed (CommandMnemonics).
        keybindings.Bind("Ctrl+Space", CommandIds.ShowMnemonics);
        keybindings.Bind("F1", CommandIds.ShowHelp);
        // Ctrl+G is a chord family (#35): L = go-to-line, P/N = previous/next cursor location.
        keybindings.Bind("Ctrl+G L", CommandIds.GoToLine);
        keybindings.Bind("Ctrl+G P", CommandIds.NavigateBack);
        keybindings.Bind("Ctrl+G N", CommandIds.NavigateForward);
        keybindings.Bind("F12", CommandIds.ShowDiagnostics);

        for (var i = 1; i <= MaxIndexedEditorBindings; i++)
            keybindings.Bind($"Ctrl+D{i}", CommandIds.FocusEditorByIndex(i));
    }

    // Flip visibility unconditionally (works from every entry point — see #85), then settle focus
    // against the NEW state: a freshly shown sidebar takes focus so it can be used; a freshly
    // hidden one that held focus hands it back to the editor so focus never sits on an invisible
    // view. We read the focus state *before* the flip — TG doesn't clear HasFocus on hide.
    private void ToggleSidebar()
    {
        var sidebarWasFocused = _workbench.Sidebar.Explorer.HasFocus || _focusLevel == FocusLevel.Sidebar;
        _workbench.ToggleSidebar();

        if (_workbench.IsSidebarVisible)
            FocusExplorer();
        else if (sidebarWasFocused)
            FocusEditorBody();
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

        var view = new SettingsView(
            _settings, _keybindings, _commands, _scopes, ApplyEditedBindings,
            _terminalIntegrations, _environment);
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

    private void OpenMnemonics()
    {
        if (_activeMnemonics is not null) return;

        // Build the dialog from the live command set joined with the hard-coded mnemonic table,
        // so it stays in step with whatever's registered (e.g. focus-tab-N) and skips commands
        // with no mnemonic (Show all commands, Show mnemonics itself).
        var entries = _commands.Registered
            .Select(c => (Command: c, Mnemonic: CommandMnemonics.For(c.Id)))
            .Where(x => x.Mnemonic is not null)
            .Select(x => new MnemonicEntry(x.Command.Id, x.Mnemonic!, x.Command.Label));

        var view = new MnemonicView(entries, commandId => _commands.TryExecute(commandId));
        view.Closed += (_, _) => CloseMnemonics(view);
        _activeMnemonics = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.SetFocus();
    }

    private void CloseMnemonics(MnemonicView view)
    {
        if (!ReferenceEquals(_activeMnemonics, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeMnemonics = null;
        FocusEditorBody();
    }

    private void OpenGoToLine()
    {
        if (_activeGoToLine is not null) return;
        if (_workbench.Editor.Group.ActiveTab is not { } tab) return;

        var totalLines = CountLines(tab.Content);
        var view = new GoToLineView(totalLines, tab.CursorRow + 1);
        view.Cancelled += (_, _) => CloseGoToLine(view);
        view.Submitted += (_, target) =>
        {
            CloseGoToLine(view);
            // Drive the move ourselves and record it as an explicit jump, so even a short
            // hop lands in history (it's a deliberate navigation) without the move event
            // overwriting the pre-jump origin we want Back to return to.
            _suppressHistory = true;
            try { tab.MoveCursor(target.Row, target.Column); }
            finally { _suppressHistory = false; }
            _history.Visit(new CursorLocation(tab.File.FullName, target.Row, target.Column), explicitJump: true);
        };

        _activeGoToLine = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusInput();
    }

    private void CloseGoToLine(GoToLineView view)
    {
        if (!ReferenceEquals(_activeGoToLine, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeGoToLine = null;
        FocusEditorBody();
    }

    private void OnEditorCursorMoved(object? sender, (IFileInfo File, int Row, int Column) e)
    {
        if (_suppressHistory) return;
        _history.Visit(new CursorLocation(e.File.FullName, e.Row, e.Column));
    }

    private void OnActiveTabChanged(object? sender, TuiCode.Editor.EditorTab? tab)
    {
        if (_suppressHistory || tab is null) return;
        _history.Visit(new CursorLocation(tab.File.FullName, tab.CursorRow, tab.CursorColumn));
    }

    private void NavigateBack() => GoToHistory(_history.GoBack());
    private void NavigateForward() => GoToHistory(_history.GoForward());

    private void GoToHistory(CursorLocation? target)
    {
        if (target is not { } loc) return;

        // Reconstruct the file from any currently-open tab's filesystem; navigating back to a
        // closed file reopens it (browser-style). A deleted file is silently skipped.
        var fileSystem = _workbench.Editor.Group.ActiveTab?.File.FileSystem;
        if (fileSystem is null) return;
        var file = fileSystem.FileInfo.New(loc.FilePath);
        if (!file.Exists) return;

        _suppressHistory = true;
        try
        {
            var tab = _workbench.Editor.Group.OpenOrFocus(file);
            tab.MoveCursor(loc.Row, loc.Column);
            tab.FocusContent();
            _focusLevel = FocusLevel.EditorBody;
        }
        finally { _suppressHistory = false; }
    }

    private void OpenFileOrFolder()
    {
        if (_activeOpen is not null) return;
        // Start browsing from the current workspace root; no root means nothing's open yet.
        if (_workbench.Sidebar.Explorer.Root is not { } root) return;

        var view = new OpenView(root);
        view.Cancelled += (_, _) => CloseOpen(view);
        view.FileSelected += (_, file) =>
        {
            CloseOpen(view);
            _workbench.OpenFile(file);
        };
        view.FolderSelected += (_, dir) =>
        {
            CloseOpen(view);
            _workbench.OpenFolder(dir);
        };

        _activeOpen = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusList();
    }

    private void CloseOpen(OpenView view)
    {
        if (!ReferenceEquals(_activeOpen, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeOpen = null;
        FocusEditorBody();
    }

    private void OpenNewPath()
    {
        if (_activeNewPath is not null) return;
        var explorer = _workbench.Sidebar.Explorer;
        // No workspace root means there's nowhere to create the entry.
        if (explorer.Root is null) return;

        var view = new NewPathView(explorer.NewEntryPrefill());
        view.Cancelled += (_, _) => CloseNewPath(view);
        view.Submitted += (_, relativePath) =>
        {
            try
            {
                var created = explorer.Create(relativePath);
                CloseNewPath(view);
                // A new file opens in the editor (VS Code behaviour); a new folder just gets
                // selected in the explorer so the user can keep building it out.
                if (created is IFileInfo file)
                    _workbench.OpenFile(file);
                else
                    FocusExplorer();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Keep the modal up so the user can correct the path.
                view.ShowError(ex.Message);
            }
        };

        _activeNewPath = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusInput();
    }

    private void CloseNewPath(NewPathView view)
    {
        if (!ReferenceEquals(_activeNewPath, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeNewPath = null;
        FocusEditorBody();
    }

    private void FocusExplorer()
    {
        if (!_workbench.IsSidebarVisible)
            _workbench.SetSidebarVisible(true);
        _workbench.Sidebar.Explorer.SetFocus();
        _focusLevel = FocusLevel.Sidebar;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        var n = 1;
        foreach (var ch in text) if (ch == '\n') n++;
        // A trailing newline shouldn't add a phantom empty line for the user-facing range.
        if (text.EndsWith('\n')) n--;
        return Math.Max(1, n);
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

    private void OpenDiagnostics()
    {
        if (_activeDiagnostics is not null) return;

        var driverName = _app.Driver?.GetName() ?? "Unknown";
        var kittyNegotiationStatus = GetKittyNegotiationStatus();
        var view = new DiagnosticsView(driverName, kittyNegotiationStatus);
        view.Closed += (_, _) => CloseDiagnostics(view);
        _activeDiagnostics = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.SetFocus();
    }

    private string GetKittyNegotiationStatus()
    {
        var flags = _app.Driver?.KittyKeyboardCapabilities?.Flags;
        if (flags is null)
            return "Unavailable";

        if (TryConvertToUInt64(flags, out var value))
            return value == 0 ? $"No ({flags})" : $"Yes ({flags})";

        return flags.ToString() ?? "Unavailable";
    }

    private static bool TryConvertToUInt64(object value, out ulong converted)
    {
        try
        {
            converted = Convert.ToUInt64(value);
            return true;
        }
        catch
        {
            converted = 0;
            return false;
        }
    }

    private void CloseDiagnostics(DiagnosticsView view)
    {
        if (!ReferenceEquals(_activeDiagnostics, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeDiagnostics = null;
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
        _workbench.Editor.Group.CursorMoved -= OnEditorCursorMoved;
        _workbench.Editor.Group.ActiveTabChanged -= OnActiveTabChanged;
        _workbench.Dispose();
        _app.Dispose();
        // Tell WezTerm the tuicode key table should be popped; matches the startup activation.
        // Emitted post-Dispose so it reaches the live terminal after TG restores it.
        Console.Out.Write("\x1b]1337;SetUserVar=TUICODE_ACTIVE=MA==\x07");
        Console.Out.Flush();
        _flowControl.Dispose();
    }

    private enum FocusLevel { Sidebar, EditorTabStrip, EditorBody }
}
