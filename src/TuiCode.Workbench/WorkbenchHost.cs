using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Terminal.Gui.Drivers;
using System.Reflection;
using Terminal.Gui.Time;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Icons;
using TuiCode.Workbench.About;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Diagnostics;
using TuiCode.Workbench.DocumentInfo;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Focus;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Grammars;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Mnemonics;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;
using TuiCode.Workbench.Themes;

namespace TuiCode.Workbench;

public sealed class WorkbenchHost : IDisposable
{
    private const int MaxIndexedEditorBindings = 9;

    // A revert bigger than a screenful can't be seen, so its message carries the way back (#245).
    private const int LargeRevert = 50;

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
    private readonly ILogger<WorkbenchHost> _logger;
    private readonly FileIcons? _icons;
    private readonly IGitCli _git;
    private readonly IGitHubCli _gitHub;
    private readonly TerminalCursors _terminalCursors;
    private readonly FindController _find;
    private readonly FocusService _focus;
    private readonly CursorLocationHistory _history = new();
    // Set while we drive the cursor ourselves (Back/Forward, Go-to-line) so those moves
    // don't get re-recorded as fresh jumps.
    private bool _suppressHistory;
    private SettingsView? _activeSettings;
    private ActionView? _activeActions;
    private HelpView? _activeHelp;
    private GoToLineView? _activeGoToLine;
    private SymbolPickerView? _activeSymbolPicker;
    private GrammarPickerView? _activeGrammarPicker;
    private DiagnosticsView? _activeDiagnostics;
    private AboutView? _activeAbout;
    private DocumentInfoView? _activeDocumentInfo;
    private SixelSupport? _sixelSupport;
    private MnemonicView? _activeMnemonics;
    private OpenView? _activeOpen;
    private PathPromptView? _activePathPrompt;
    private ConfirmView? _activeConfirm;
    private RevisionPickerView? _activeRevisionPicker;
    private PullRequestPickerView? _activePullRequestPicker;
    private SubmitReviewView? _activeSubmitReview;
    private CommentView? _activeComment;
    private DraftComments? _draftComments;
    private bool _launchedFromExplorer;
    private bool _sidebarDragging;
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
        string? driverName = null,
        ILogger<WorkbenchHost>? logger = null,
        FileIcons? icons = null,
        IGitCli? git = null,
        IGitHubCli? gitHub = null)
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
        WriteToTerminal("\x1b]1337;SetUserVar=TUICODE_ACTIVE=MQ==\x07");
        _terminalCursors = new TerminalCursors(_app);
        _terminalCursors.Detect();
        // Detected up front so About can show a spinner rather than ASCII art that the image then replaces.
        if (_app.Driver is { } driver)
            SixelProbe.Detect(driver, support => _app.Invoke(() =>
            {
                _sixelSupport = support;
                _activeAbout?.Present(support);
            }));
        _workbench = workbench;
        _commands = commands;
        _keybindings = keybindings;
        _scopes = scopes;
        _settings = settings;
        _terminalIntegrations = (terminalIntegrations ?? Array.Empty<ITerminalIntegration>()).ToArray();
        _environment = environment ?? new SystemEnvironment();
        _logger = logger ?? NullLogger<WorkbenchHost>.Instance;
        _icons = icons;
        _git = git ?? new GitCli(new FileSystem());
        _gitHub = gitHub ?? new GitHubCli();
        _focus = new FocusService(FocusedView);

        RegisterDefaultCommands();
        ApplyKeybindings(_settings.KeybindingOverrides);
        _workbench.Editor.Group.Settings = _settings.Editor;
        _workbench.SetSidebarWidth(_settings.SidebarWidth);
        ApplyTokenTheme();
        _settings.ThemeChanged += (_, _) => ApplyTokenTheme();

        RegisterFocusRegions();

        // Workbench scope is the bottom of the input stack; never popped. The find bar layers above it while it's open.
        _keybindings.FocusedScope = FocusedScope;
        _scopes.Push(_keybindings);
        _find = new FindController(_workbench.Editor.Group, _scopes, _keybindings);
        _find.Closed += (_, _) => FocusEditorBody();
        _find.HintChanged += (_, hint) => _workbench.StatusBar.SetHint(hint);

        _app.Keyboard.KeyDown += OnAppKeyDown;
        _app.Mouse.MouseEvent += OnAppMouseEvent;
        // TG raises no event for most programmatic cursor moves (find, multi-caret, line moves), so poll.
        _app.Iteration += OnIteration;
        _keybindings.ChordChanged += OnChordChanged;

        // Feed the cursor-location history (#35): within-file moves come from CursorMoved,
        // file switches (manual tab cycling, opening a file) from ActiveTabChanged. The
        // history's own heuristic decides which of these count as navigable jumps.
        _workbench.Editor.Group.CursorMoved += OnEditorCursorMoved;
        _workbench.Editor.Group.ActiveTabChanged += OnActiveTabChanged;
        _workbench.Sidebar.Review.FileActivated += (_, e) => OpenReviewDiff(e.Review, e.Change);
        _workbench.Sidebar.Review.PullRequestActivated += (_, review) => OpenOverview(review);
        _workbench.Sidebar.Review.OutdatedThreadsActivated += (_, e) => OpenOutdatedThreads(e.Review, e.Node);
        _workbench.Sidebar.Review.ThreadsLoaded += (_, review) => ShowCommentsInOpenDiffs(review);
        // The review pane rebuilds on a save, a sidebar switch and each stage of its background load, which
        // can take Terminal.Gui's focus with it; nobody asked it to, so the keys go back (#228).
        _workbench.Sidebar.Review.Refreshed += (_, _) => SettleFocusAfterRedraw();
        // Opening a file is a move into the Editor region, so the service makes it: the tab it opens may
        // still be carrying a stale HasFocus, which its own SetFocus would no-op on (#228).
        _workbench.FileOpened += (_, _) => MoveFocus(FocusRegion.Editor);
    }

    private void RegisterFocusRegions()
    {
        var sidebar = _workbench.Sidebar;
        var group = _workbench.Editor.Group;
        var sidebarBorder = new FocusBorder(sidebar);
        var editorBorder = new FocusBorder(_workbench.Editor);

        _focus.Register(FocusRegion.Find, () => Take(sidebar.Search, sidebar.Search.FocusQuery), focused => Owns(sidebar.Search, focused));
        _focus.Register(FocusRegion.Review, () => Take(sidebar.Review, sidebar.Review.FocusList), focused => Owns(sidebar.Review, focused));
        _focus.Register(FocusRegion.Explorer, () => Take(sidebar.Explorer), focused => Owns(sidebar.Explorer, focused));
        // The find bar sits inside the active tab, so the editor regions have to let it through (#229).
        _focus.Register(FocusRegion.FindBar, () => Take(_find.Bar, _find.FocusInput), OnFindBar);
        _focus.Register(FocusRegion.Diff, () => Take(group.ActiveDiffTab),
            focused => group.ActiveDiffTab is { } diff && Owns(diff, focused));
        _focus.Register(FocusRegion.Editor, () => Take(group.Value, group.FocusActive),
            focused => group.ActiveDiffTab is null && Owns(_workbench.Editor, focused)
                && !OnTabStrip(group, focused) && !OnFindBar(focused));
        // Reached by ft and by TG's own navigation (#237); owning the editor pane is what keeps cycling tabs in it.
        _focus.Register(FocusRegion.Tabs, () => true, focused => Owns(_workbench.Editor, focused) && !OnFindBar(focused));

        _focus.RegionChanged += (_, region) =>
        {
            var inSidebar = region is FocusRegion.Explorer or FocusRegion.Find or FocusRegion.Review;
            _workbench.StatusBar.SetFocusRegion(FocusService.Label(region));
            sidebarBorder.Show(inSidebar);
            editorBorder.Show(!inSidebar);
        };
        _workbench.StatusBar.SetFocusRegion(FocusService.Label(_focus.Region));
        editorBorder.Show(true);
    }

    private bool OnFindBar(object? focused) => Owns(_find.Bar, focused);

    private static bool Owns(View region, object? focused) =>
        focused is View view && View.IsInHierarchy(region, view, includeAdornments: true);

    /// <summary>
    /// Hands the keyboard to <paramref name="view"/>. Terminal.Gui 2.1.0 leaves <c>HasFocus</c> set on a view
    /// the keyboard has left, and its <c>SetFocus</c> is a no-op on a view that believes it still has focus —
    /// which is what made a diff unreachable for the rest of the session (#197).
    /// </summary>
    private bool Take(View? view, Func<bool>? move = null)
    {
        if (view is null) return false;
        if (view.HasFocus && !Owns(view, FocusedView())) view.HasFocus = false;
        return move is null ? view.SetFocus() : move();
    }

    // GetFocused can report an ancestor of the view holding the keyboard, so walk down to it (AGENTS.md).
    private View? FocusedView() => _app.Navigation?.GetFocused() is { } focused ? focused.MostFocused ?? focused : null;

    // TG draws each tab's header in that tab's border, so a header holding the keyboard is in the tab's hierarchy but not its content.
    private static bool OnTabStrip(EditorGroup group, object? focused) =>
        group.Value is { } tab && focused is View view && !ReferenceEquals(view, tab)
            && !View.IsInHierarchy(tab, view, includeAdornments: false);

    private void ApplyTokenTheme()
    {
        if (_workbench.Editor.Group.Syntax is not { } syntax) return;
        syntax.UseTheme(BundledThemes.TokenThemeFor(_settings.Theme));
        foreach (var diff in _workbench.Editor.Group.DiffTabs) diff.SetNeedsDraw();
        // OSC 12 sets the terminal's cursor colour, which no TG scheme covers; terminals without it ignore the sequence.
        if (syntax.EditorColors.TryGetValue("editorCursor.foreground", out var hex) && Color.TryParse(hex, out Color? cursor))
            WriteToTerminal($"\x1b]12;#{cursor.Value.R:X2}{cursor.Value.G:X2}{cursor.Value.B:X2}\x07");
    }

    private static void WriteToTerminal(string sequence)
    {
        Console.Out.Write(sequence);
        Console.Out.Flush();
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
            && _focus.Region == FocusRegion.Tabs
            && TryHandleTabStripKey(key))
            key.Handled = true;
    }

    // Handled here short-circuits App.RaiseMouseEvent before any view sees the event, which is what keeps
    // the drag off the editor's cursor, the trees' selection and focus (#252, and see AGENTS.md).
    private void OnAppMouseEvent(object? sender, Mouse mouse)
    {
        if (_sidebarDragging)
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased))
            {
                _sidebarDragging = false;
                ReportSidebarWidth(DragSidebarTo(mouse.ScreenPosition.X));
            }
            else if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
            {
                DragSidebarTo(mouse.ScreenPosition.X);
            }
            else
            {
                // The button went up out of sight; drop the drag rather than fold the next press into it.
                _sidebarDragging = false;
                return;
            }
            mouse.Handled = true;
            return;
        }

        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) || !StartsSidebarDrag(mouse)) return;
        _sidebarDragging = true;
        mouse.Handled = true;
    }

    // TG reports the deepest view under the pointer, so requiring the sidebar's own border also rules
    // out a modal covering that column.
    private bool StartsSidebarDrag(Mouse mouse) =>
        mouse.View is AdornmentView { Adornment: { } adornment }
        && ReferenceEquals(adornment.Parent, _workbench.Sidebar)
        && _workbench.IsOnSidebarBorder(mouse.ScreenPosition.X);

    private int DragSidebarTo(int column) =>
        _workbench.ResizeSidebarTo(_workbench.SidebarWidthForBorderAt(column));

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

    private CommandScope FocusedScope()
    {
        // A mouse click lands between iterations, so re-read the region rather than reuse the last one.
        _focus.Reconcile();
        return FocusService.ScopeOf(_focus.Region);
    }

    private void RegisterDefaultCommands()
    {
        _commands.Register(CommandIds.Quit, "Quit", () => _app.RequestStop());
        _commands.Register(CommandIds.SaveActiveEditor, "Save active editor", () => _workbench.Editor.Save());
        _commands.Register(CommandIds.CloseActiveEditor, "Close active editor", () => _workbench.Editor.CloseActive());
        _commands.Register(CommandIds.NextEditor, "Next tab", () => _workbench.Editor.NextTab());
        _commands.Register(CommandIds.PreviousEditor, "Previous tab", () => _workbench.Editor.PreviousTab());

        _commands.Register(CommandIds.ToggleSidebar, "Toggle sidebar", ToggleSidebar);
        _commands.Register(CommandIds.WidenSidebar, "Widen sidebar", () => NudgeSidebar(SidebarSizing.Step));
        _commands.Register(CommandIds.NarrowSidebar, "Narrow sidebar", () => NudgeSidebar(-SidebarSizing.Step));
        _commands.Register(CommandIds.FocusSidebar, "Focus sidebar", FocusSidebar);
        _commands.Register(CommandIds.ShowExplorer, "Show explorer", () => ShowSidebarTab(SidebarTab.Explorer));
        _commands.Register(CommandIds.FindGlobally, "Find globally", () => OpenFindPane(replace: false));
        _commands.Register(CommandIds.ReplaceGlobally, "Replace globally", () => OpenFindPane(replace: true));
        // No default key (#180).
        _commands.Register(CommandIds.FocusReview, "Focus review", FocusReview);
        _commands.Register(CommandIds.FindInFile, "Find in file", () => OpenFind(replace: false));
        _commands.Register(CommandIds.ReplaceInFile, "Replace in file", () => OpenFind(replace: true));
        _commands.Register(CommandIds.FocusEditorBody, "Focus editor", FocusEditorBody);
        _commands.Register(CommandIds.FocusEditorTabStrip, "Focus editor tab strip", FocusEditorTabStrip);
        _commands.Register(CommandIds.ToggleGutter, "Toggle gutter", ToggleGutter);
        _commands.Register(CommandIds.ToggleColumnSelect, "Toggle column select", ToggleColumnSelect);
        _commands.Register(CommandIds.OpenSettings, "Open settings", OpenSettings);
        _commands.Register(CommandIds.Open, "Open file or folder", OpenFileOrFolder);
        // No default key (#184, #185, #187, #188).
        _commands.Register(CommandIds.OpenPullRequest, "Open pull request", OpenPullRequest);
        _commands.Register(CommandIds.PullRequestOverview, "PR overview", ShowPullRequestOverview);
        _commands.Register(CommandIds.SubmitReview, "Submit review", SubmitReview);
        _commands.Register(CommandIds.CreateComment, "Create comment", CreateComment);
        _commands.Register(CommandIds.New, "New file or folder", OpenNewPath);
        var explorer = _workbench.Sidebar.Explorer;
        _commands.Register(CommandIds.DeleteFile, "Delete file or folder", ConfirmDelete, CommandScope.Explorer);
        _commands.Register(CommandIds.RenameFile, "Move or rename file or folder", OpenRename, CommandScope.Explorer);
        _commands.Register(CommandIds.CutFile, "Cut file or folder", CutEntry, CommandScope.Explorer);
        _commands.Register(CommandIds.PasteFile, "Paste file or folder", PasteEntry, CommandScope.Explorer);
        _commands.Register(CommandIds.CancelCut, "Cancel cut", explorer.ClearCut, CommandScope.Explorer, () => explorer.PendingCut is not null);
        explorer.CutCleared += (_, item) => _workbench.StatusBar.ClearMessage(CutMessage(item));
        var search = _workbench.Sidebar.Search;
        // Find covers the whole pane, results list included; these keys belong to the query/replace inputs only.
        _commands.Register(CommandIds.SearchFocusResults, "Focus find results", () => search.FocusResults(),
            CommandScope.Find, () => search.InputsHaveFocus);
        _commands.Register(CommandIds.SearchSwitchField, "Switch find field", search.SwitchField,
            CommandScope.Find, () => search.InputsHaveFocus);
        _commands.Register(CommandIds.SearchReplaceAll, "Replace all globally",
            () => { if (search.ReplaceVisible) search.RequestReplaceAll(); }, CommandScope.Find, () => search.InputsHaveFocus);
        _commands.Register(CommandIds.ShowActions, "Show all commands", OpenActions);
        _commands.Register(CommandIds.ShowMnemonics, "Show mnemonics", OpenMnemonics);
        _commands.Register(CommandIds.ShowHelp, "Getting Started (help)", OpenHelp);
        _commands.Register(CommandIds.GoToLine, "Go to line:column", OpenGoToLine);
        // No default key (#137): VS Code's Ctrl+Shift+O collapses onto Ctrl+O in Terminal.app.
        _commands.Register(CommandIds.GoToSymbol, "Go to symbol in file", OpenSymbolPicker, CommandScope.Editor);
        // No default key (#21): rarely needed, and users can bind one in Settings.
        _commands.Register(CommandIds.ChangeGrammar, "Change grammar", OpenGrammarPicker);
        _commands.Register(CommandIds.NavigateBack, "Previous cursor position", NavigateBack);
        _commands.Register(CommandIds.NavigateForward, "Next cursor position", NavigateForward);
        _commands.Register(CommandIds.ShowDiagnostics, "Show diagnostics", OpenDiagnostics);
        _commands.Register(CommandIds.ShowAbout, "About TuiCode", OpenAbout);
        _commands.Register(CommandIds.ShowDocumentInfo, "Show document info", OpenDocumentInfo);
        // No default key (#61): users can bind one in Settings.
        _commands.Register(CommandIds.CompareToSaved, "Compare to saved", CompareToSaved);
        _commands.Register(CommandIds.NextChange, "Next change", () => MoveToChange(1), CommandScope.Diff);
        _commands.Register(CommandIds.PreviousChange, "Previous change", () => MoveToChange(-1), CommandScope.Diff);
        _commands.Register(CommandIds.GoToChangeLine, "Go to line in file", GoToChangeLine, CommandScope.Diff);
        _commands.Register(CommandIds.RevertChange, "Revert change", RevertChange, CommandScope.Diff);
        _commands.Register(CommandIds.RevertAllChanges, "Revert all changes in file", RevertAllChanges, CommandScope.Diff);
        _commands.Register(CommandIds.ScrollDiffLeft, "Scroll diff left", () => ScrollDiff(-1), CommandScope.Diff);
        _commands.Register(CommandIds.ScrollDiffRight, "Scroll diff right", () => ScrollDiff(1), CommandScope.Diff);
        _commands.Register(CommandIds.ScrollDiffPageLeft, "Scroll diff a page left", () => ScrollDiff(-1, page: true), CommandScope.Diff);
        _commands.Register(CommandIds.ScrollDiffPageRight, "Scroll diff a page right", () => ScrollDiff(1, page: true), CommandScope.Diff);
        _commands.Register(CommandIds.CompareToRevision, "Compare to revision", CompareToRevision);
        _commands.Register(CommandIds.CompareToOtherFile, "Compare to other file", CompareToOtherFile);
        _commands.Register(CommandIds.MoveLinesUp, "Move line up", () => EditActiveTab(tab => tab.MoveLines(LineDirection.Up)));
        _commands.Register(CommandIds.MoveLinesDown, "Move line down", () => EditActiveTab(tab => tab.MoveLines(LineDirection.Down)));
        _commands.Register(CommandIds.DuplicateLinesUp, "Duplicate line up", () => EditActiveTab(tab => tab.DuplicateLines(LineDirection.Up)));
        _commands.Register(CommandIds.DuplicateLinesDown, "Duplicate line down", () => EditActiveTab(tab => tab.DuplicateLines(LineDirection.Down)));
        _commands.Register(CommandIds.AddCursorAbove, "Add cursor above", () => EditActiveTab(tab => tab.AddCursor(LineDirection.Up)));
        _commands.Register(CommandIds.AddCursorBelow, "Add cursor below", () => EditActiveTab(tab => tab.AddCursor(LineDirection.Down)));
        var group = _workbench.Editor.Group;
        _commands.Register(CommandIds.RemoveSecondaryCursors, "Remove secondary cursors", () => group.ActiveTab?.RemoveSecondaryCursors(),
            CommandScope.Editor, () => group.ActiveTab is { HasSecondaryCursors: true });
        // No default keys (#113).
        _commands.Register(CommandIds.SelectNextOccurrence, "Select next occurrence", () => EditActiveTab(tab => tab.SelectNextOccurrence()));
        _commands.Register(CommandIds.SelectPreviousOccurrence, "Select previous occurrence", () => EditActiveTab(tab => tab.SelectPreviousOccurrence()));
        _commands.Register(CommandIds.SelectAllOccurrences, "Select all occurrences", () => EditActiveTab(tab => tab.SelectAllOccurrences()));

        for (var i = 1; i <= MaxIndexedEditorBindings; i++)
        {
            var index = i;
            _commands.Register(CommandIds.FocusEditorByIndex(index), $"Focus editor tab {index}", () => FocusEditorAt(index - 1));
        }
    }

    /// <summary>
    /// Replace the entire workbench binding set: clear, re-apply defaults, then layer on the user's
    /// overrides, each in its command's scope. Called at startup and again when the settings UI commits a change.
    /// </summary>
    public void ApplyKeybindings(IEnumerable<KeybindingOverride> overrides)
    {
        _keybindings.Reset();
        BindDefaults(_keybindings);

        foreach (var o in overrides)
        {
            // A hand-edited keybindings file can carry a malformed entry — an empty chord or command.
            // Bind/Unbind throw ArgumentException on those; skip the offending entry so one bad line
            // can't crash startup (#90), and log it rather than failing silently. Defaults stay strict
            // (they're hardcoded — a parse failure there is our bug, not the user's), so only
            // user-supplied overrides get this tolerance. Where these logs surface is a follow-up (#92).
            try
            {
                if (o.IsRemoval) _keybindings.Unbind(o.Keys, _commands.ScopeOf(o.EffectiveCommand));
                else _keybindings.Bind(o.Keys, o.EffectiveCommand);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Ignored invalid keybinding override {Chord} for command {Command}", KeyChord.Display(o.Keys), o.Command);
            }
        }

        var help = _keybindings.Bindings.FirstOrDefault(b => b.CommandId == CommandIds.ShowHelp);
        _workbench.StatusBar.SetIdleHint(help is null ? null : $"Press {help.Display} for help");
        _workbench.DiffKeysHint = DiffKeys("revert");
        _workbench.DeletedDiffKeysHint = DiffKeys("restore");

        string DiffKeys(string revert) => string.Join("  ", new[]
        {
            KeyHint(CommandIds.NextChange, "next"),
            KeyHint(CommandIds.PreviousChange, "prev"),
            KeyHint(CommandIds.RevertChange, revert),
            KeyHint(CommandIds.GoToChangeLine, "go to line"),
        }.OfType<string>());
    }

    private string? KeyHint(string commandId, string label) =>
        _keybindings.Bindings.FirstOrDefault(b => b.CommandId == commandId) is { } binding ? $"{binding.Display} {label}" : null;

    /// <summary>
    /// Take the picker's edited binding set, compute the diff against defaults, persist as the
    /// new override list, and apply it to the live keybinding service.
    /// </summary>
    public void ApplyEditedBindings(IEnumerable<KeyBinding> editedBindings)
    {
        // Diff defaults against the edited set by scope and canonical (keycode) identity, not display string —
        // a chord whose ToString() can't round-trip (e.g. Ctrl+Alt+Shift++) must still diff cleanly (#89).
        var defaults = GetDefaultBindings().ToDictionary(b => (b.Scope, b.CanonicalId));
        var edited = editedBindings.ToDictionary(b => (b.Scope, b.CanonicalId));

        var overrides = new List<KeybindingOverride>();
        foreach (var id in defaults.Keys.Union(edited.Keys))
        {
            var hasDefault = defaults.TryGetValue(id, out var def);
            var hasEdited = edited.TryGetValue(id, out var ed);

            if (hasDefault && !hasEdited)
                overrides.Add(new KeybindingOverride(def!.Chord, "-" + def.CommandId));
            else if (hasEdited && (!hasDefault || !string.Equals(def!.CommandId, ed!.CommandId, StringComparison.Ordinal)))
                overrides.Add(new KeybindingOverride(ed!.Chord, ed.CommandId));
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
        var keybindings = new KeybindingService(_commands);
        BindDefaults(keybindings);
        return keybindings.Bindings.ToArray();
    }

    private static void BindDefaults(IKeybindingService keybindings)
    {
        keybindings.Bind("Ctrl+Q", CommandIds.Quit);
        keybindings.Bind("Ctrl+S", CommandIds.SaveActiveEditor);
        keybindings.Bind("Ctrl+W", CommandIds.CloseActiveEditor);
        // Not Ctrl+Tab: Terminal.app and iTerm2 both keep it for their own tabs (#254).
        keybindings.Bind("Alt+Tab", CommandIds.NextEditor);
        keybindings.Bind("Alt+Shift+Tab", CommandIds.PreviousEditor);

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
        keybindings.Bind("Alt+CursorUp", CommandIds.MoveLinesUp);
        keybindings.Bind("Alt+CursorDown", CommandIds.MoveLinesDown);
        keybindings.Bind("Alt+Shift+CursorUp", CommandIds.DuplicateLinesUp);
        keybindings.Bind("Alt+Shift+CursorDown", CommandIds.DuplicateLinesDown);
        keybindings.Bind("Ctrl+Alt+CursorUp", CommandIds.AddCursorAbove);
        keybindings.Bind("Ctrl+Alt+CursorDown", CommandIds.AddCursorBelow);
        keybindings.Bind("Ctrl+T C", CommandIds.ToggleColumnSelect);
        keybindings.Bind("Ctrl+F", CommandIds.FindInFile);
        keybindings.Bind("Ctrl+H", CommandIds.ReplaceInFile);
        // Ctrl+Shift+letter needs a terminal that doesn't collapse it onto Ctrl+letter (see AGENTS.md).
        keybindings.Bind("Ctrl+Shift+F", CommandIds.FindGlobally);
        keybindings.Bind("Ctrl+Shift+H", CommandIds.ReplaceGlobally);
        keybindings.Bind("Ctrl+Shift+E", CommandIds.ShowExplorer);
        keybindings.Bind("Ctrl+Shift+R", CommandIds.FocusReview);

        for (var i = 1; i <= MaxIndexedEditorBindings; i++)
            keybindings.Bind($"Ctrl+D{i}", CommandIds.FocusEditorByIndex(i));

        keybindings.Bind("Esc", CommandIds.RemoveSecondaryCursors);

        keybindings.Bind("Delete", CommandIds.DeleteFile);
        // Our iTerm2 profile sends forward-delete as ^D (Iterm2Integration).
        keybindings.Bind("Ctrl+D", CommandIds.DeleteFile);
        keybindings.Bind("F2", CommandIds.RenameFile);
        keybindings.Bind("Ctrl+X", CommandIds.CutFile);
        keybindings.Bind("Ctrl+V", CommandIds.PasteFile);
        keybindings.Bind("Esc", CommandIds.CancelCut);

        keybindings.Bind("Alt+CursorDown", CommandIds.NextChange);
        keybindings.Bind("Alt+CursorUp", CommandIds.PreviousChange);
        keybindings.Bind("Enter", CommandIds.GoToChangeLine);
        keybindings.Bind("Ctrl+R", CommandIds.RevertChange);
        keybindings.Bind("CursorLeft", CommandIds.ScrollDiffLeft);
        keybindings.Bind("CursorRight", CommandIds.ScrollDiffRight);
        keybindings.Bind("Shift+CursorLeft", CommandIds.ScrollDiffPageLeft);
        keybindings.Bind("Shift+CursorRight", CommandIds.ScrollDiffPageRight);

        keybindings.Bind("Enter", CommandIds.SearchFocusResults);
        keybindings.Bind("CursorDown", CommandIds.SearchFocusResults);
        keybindings.Bind("Tab", CommandIds.SearchSwitchField);
        keybindings.Bind("Shift+Tab", CommandIds.SearchSwitchField);
        keybindings.Bind("Ctrl+Enter", CommandIds.SearchReplaceAll);
    }

    // Flip visibility unconditionally (works from every entry point — see #85), then settle focus
    // against the NEW state: a freshly shown sidebar takes focus so it can be used; a freshly
    // hidden one that held focus hands it back to the editor so focus never sits on an invisible
    // view. We read the focus state *before* the flip — TG doesn't clear HasFocus on hide.
    private void ToggleSidebar()
    {
        var sidebarWasFocused = _focus.Region is FocusRegion.Explorer or FocusRegion.Find or FocusRegion.Review;
        _workbench.ToggleSidebar();

        if (_workbench.IsSidebarVisible)
            FocusSidebar();
        else if (sidebarWasFocused)
            FocusEditorBody();
    }

    private void NudgeSidebar(int columns)
    {
        if (!_workbench.IsSidebarVisible)
        {
            _workbench.StatusBar.SetMessage($"Sidebar is hidden — use {CommandMnemonics.For(CommandIds.ToggleSidebar)} to show it");
            return;
        }

        ReportSidebarWidth(_workbench.NudgeSidebarWidth(columns));
    }

    private void ReportSidebarWidth(int width)
    {
        _settings.SidebarWidth = width;
        _settings.Save();
        _workbench.StatusBar.SetMessage($"Sidebar width: {width}{LimitSuffix(width)}");
    }

    private string LimitSuffix(int width) =>
        width <= SidebarSizing.Min ? " (minimum)"
        : width >= SidebarSizing.MaxFor(_workbench.Viewport.Width)
            ? $" (maximum — the editor needs {SidebarSizing.EditorFloor} columns)"
            : string.Empty;

    private void ToggleGutter()
    {
        var group = _workbench.Editor.Group;
        group.GutterVisible = !group.GutterVisible;
    }

    // Unlike the gutter this mode is invisible until a selection is swept, so flag it in the status bar.
    private void ToggleColumnSelect()
    {
        var group = _workbench.Editor.Group;
        group.ColumnSelect = !group.ColumnSelect;
        _workbench.StatusBar.SetMode(group.ColumnSelect ? "Column select" : null);
        FocusEditorBody();
    }

    // A sidebar item's shortcut (#33) shows its tab, revealing the sidebar if needed, and never hides it:
    // only ts does (#259). Like ToggleSidebar it decides on visibility, not focus, so it behaves the same
    // from the palette and leader (#85).
    private void ShowSidebarTab(SidebarTab tab)
    {
        _workbench.Sidebar.ShowTab(tab);
        FocusSidebar();
    }

    private void FocusReview()
    {
        var showing = _workbench.IsSidebarVisible && _workbench.Sidebar.ActiveTab == SidebarTab.Review;
        _workbench.Sidebar.ShowTab(SidebarTab.Review);
        FocusSidebar();
        if (showing) _workbench.Sidebar.Review.Refresh();
    }

    // Ctrl+Shift+F is also how you leave a replace behind: like the bar's Ctrl+F it shows the pane with only
    // the find row.
    private void OpenFindPane(bool replace)
    {
        _workbench.Sidebar.Search.ShowReplace(replace);
        _workbench.Sidebar.ShowTab(SidebarTab.Find);
        FocusSidebar();
        if (replace) _workbench.Sidebar.Search.FocusReplacement();
    }

    /// <summary>
    /// Find in the active file (#229). The keys land in the bar's inputs from wherever they were; with no file
    /// tab to search they go to the find pane instead, and on a tab that can't be searched they stay put and
    /// the status bar says why rather than the key doing nothing at all.
    /// </summary>
    private void OpenFind(bool replace)
    {
        var group = _workbench.Editor.Group;
        if (group.ActiveTab is null)
        {
            if (group.Value is null)
            {
                OpenFindPane(replace);
                return;
            }
            var what = group.ActiveDiffTab is null ? "a document" : "a diff";
            _workbench.StatusBar.SetMessage(replace
                ? $"Nothing to replace in {what} — Ctrl+H needs a file tab"
                : $"Nothing to find in {what} — Ctrl+F needs a file tab");
            return;
        }
        _find.Open(replace);
        MoveFocus(FocusRegion.FindBar);
    }

    private void FocusEditorBody() =>
        MoveFocus(_workbench.Editor.Group.ActiveDiffTab is null ? FocusRegion.Editor : FocusRegion.Diff);

    /// <summary>Every focus move the workbench makes; one that can't land says so rather than going quiet (#228).</summary>
    private bool MoveFocus(FocusRegion region)
    {
        if (_focus.Focus(region)) return true;
        _workbench.StatusBar.SetMessage($"Nothing to focus in {FocusService.Label(region)}");
        return false;
    }

    /// <summary>
    /// Re-asserts the record after something moved Terminal.Gui's focus without being asked (#228). A region
    /// that still holds the keys keeps them, so a refresh doesn't drag them off the part of it they're on —
    /// unless that's the bare review pane, whose file list a rebuild takes the keys off and can't give back.
    /// </summary>
    private void SettleFocusAfterRedraw()
    {
        var review = _workbench.Sidebar.Review;
        if (_focus.Region == FocusRegion.Review && !review.ListHasFocus && !review.OverviewHasFocus)
            _focus.Focus(FocusRegion.Review);
        else if (!_focus.Holds)
            _focus.Focus(_focus.Region);
    }

    /// <summary>
    /// Where a modal was opened from: no region owns a modal, so the record still names the region that had
    /// the keys. The editor takes them when that region has gone since, and only then is there anything to say.
    /// </summary>
    private void FocusCallingRegion()
    {
        if (!_focus.Focus(_focus.Region)) FocusEditorBody();
    }

    private void EditActiveTab(Action<EditorTab> edit)
    {
        if (_workbench.Editor.Group.ActiveTab is not { } tab) return;
        edit(tab);
        FocusEditorBody();
    }

    private void FocusEditorTabStrip()
    {
        // Only meaningful from the editor body, with a file tab to cycle; from anywhere else it's a no-op.
        if (_focus.Region != FocusRegion.Editor || _workbench.Editor.Group.ActiveTab is null) return;
        MoveFocus(FocusRegion.Tabs);
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
            _terminalIntegrations, _environment, _workbench.Editor.Group.Syntax, ApplyGrammarAssociations, _icons);
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
        _workbench.Editor.Group.Settings = _settings.Editor;
        _workbench.SetSidebarWidth(_settings.SidebarWidth);
        FocusCallingRegion();
    }

    private void ApplyGrammarAssociations()
    {
        if (_workbench.Editor.Group.Syntax is not { } syntax) return;
        syntax.Associations = _settings.GrammarAssociations;
        _workbench.Editor.Group.InferGrammars();
    }

    private void OpenGrammarPicker()
    {
        if (_activeGrammarPicker is not null) return;
        if (_workbench.Editor.Group is not { ActiveTab: { HasSyntax: true } tab, Syntax: { } syntax }) return;

        var view = new GrammarPickerView(syntax.Languages, $"Grammar for {tab.File.Name}", tab.Grammar);
        view.Chosen += (_, grammar) => tab.SetGrammar(grammar);
        view.Closed += (_, _) => CloseGrammarPicker(view);
        _activeGrammarPicker = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusSearch();
    }

    private void CloseGrammarPicker(GrammarPickerView view)
    {
        if (!ReferenceEquals(_activeGrammarPicker, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeGrammarPicker = null;
        FocusCallingRegion();
    }

    private void OpenActions()
    {
        if (_activeActions is not null) return;

        var fromExplorer = _workbench.Sidebar.Explorer.HasFocus;
        var view = new ActionView(_commands, _keybindings, commandId => RunLaunched(commandId, fromExplorer));
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
        FocusCallingRegion();
    }

    // Launchers close, handing focus to the editor, before they run their command; remember whether the
    // explorer had focus so file commands still act on its selection.
    private void RunLaunched(string commandId, bool fromExplorer)
    {
        _launchedFromExplorer = fromExplorer;
        try { _commands.TryExecute(commandId); }
        finally { _launchedFromExplorer = false; }
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

        var fromExplorer = _workbench.Sidebar.Explorer.HasFocus;
        var view = new MnemonicView(entries, commandId => RunLaunched(commandId, fromExplorer));
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
        FocusCallingRegion();
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
        FocusCallingRegion();
    }

    /// <summary>
    /// Go to symbol (<c>gs</c>): the definitions in the active file. The scan is the tab's, cached against its
    /// buffer, so a second <c>gs</c> without an edit opens on a finished list; a fresh one fills in from
    /// <see cref="OnIteration"/>.
    /// </summary>
    private void OpenSymbolPicker()
    {
        if (_activeSymbolPicker is not null) return;
        if (_workbench.Editor.Group.ActiveTab is not { } tab)
        {
            _workbench.StatusBar.SetMessage("No file open");
            return;
        }
        if (tab.ScanSymbols() is not { } scan)
        {
            _workbench.StatusBar.SetMessage(SymbolPickerView.NoSymbols);
            return;
        }

        var view = new SymbolPickerView(tab.File.Name, scan, _icons);
        view.Cancelled += (_, _) => CloseSymbolPicker(view);
        view.Submitted += (_, symbol) =>
        {
            CloseSymbolPicker(view);
            // As in Go to line: drive the move ourselves so even a short hop records as a deliberate jump.
            _suppressHistory = true;
            try
            {
                tab.MoveCursor(symbol.Line, 0);
                tab.CenterOnCursor();
            }
            finally { _suppressHistory = false; }
            _history.Visit(new CursorLocation(tab.File.FullName, symbol.Line, 0), explicitJump: true);
        };

        _activeSymbolPicker = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusFilter();
    }

    private void CloseSymbolPicker(SymbolPickerView view)
    {
        if (!ReferenceEquals(_activeSymbolPicker, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeSymbolPicker = null;
        FocusCallingRegion();
    }

    private void OnIteration(object? sender, EventArgs<IApplication?> e)
    {
        _workbench.ShowCursorPosition();
        // Picks up focus Terminal.Gui moved on its own, and any move that didn't land where it was asked to.
        _focus.Reconcile();
        _activeSymbolPicker?.Advance();
    }

    private void OnEditorCursorMoved(object? sender, (IFileInfo File, int Row, int Column) e)
    {
        if (_suppressHistory) return;
        _history.Visit(new CursorLocation(e.File.FullName, e.Row, e.Column));
    }

    private void OnActiveTabChanged(object? sender, TuiCode.Editor.EditorTab? tab)
    {
        _find.OnActiveTabChanged(tab);
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
            MoveFocus(FocusRegion.Editor);
        }
        finally { _suppressHistory = false; }
    }

    private void OpenFileOrFolder()
    {
        if (_activeOpen is not null) return;
        // Start browsing from the current workspace root; no root means nothing's open yet.
        if (_workbench.Sidebar.Explorer.Root is not { } root) return;

        var view = new OpenView(root, _icons);
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
        FocusCallingRegion();
    }

    private void OpenNewPath()
    {
        if (_activePathPrompt is not null) return;
        var explorer = _workbench.Sidebar.Explorer;
        // No workspace root means there's nowhere to create the entry.
        if (explorer.Root is null) return;

        var view = new PathPromptView(
            "New File or Folder", "Path (relative to root; end with / for a folder)", explorer.NewEntryPrefill());
        view.Cancelled += (_, _) => ClosePathPrompt(view);
        view.Submitted += (_, relativePath) =>
        {
            try
            {
                var created = explorer.Create(relativePath);
                ClosePathPrompt(view);
                // A new file opens in the editor (VS Code behaviour); a new folder just gets
                // selected in the explorer so the user can keep building it out.
                if (created is IFileInfo file)
                    _workbench.OpenFile(file);
                else
                {
                    _workbench.Sidebar.ShowTab(SidebarTab.Explorer);
                    FocusSidebar();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Keep the modal up so the user can correct the path.
                view.ShowError(ex.Message);
            }
        };

        ShowPathPrompt(view);
    }

    private void OpenRename()
    {
        if (_activePathPrompt is not null) return;
        var fromExplorer = ExplorerIsContext;
        if (FileCommandTarget(fromExplorer, "renamed") is not { } item) return;

        var view = new PathPromptView("Move or Rename", "Path (relative to root)", _workbench.Sidebar.Explorer.RelativePath(item));
        view.SelectName(item is IDirectoryInfo);
        view.Cancelled += (_, _) =>
        {
            ClosePathPrompt(view);
            if (fromExplorer) FocusSidebar();
        };
        view.Submitted += (_, relativePath) =>
        {
            try
            {
                var moved = _workbench.Move(item, relativePath);
                _history.Rebase(item.FullName, moved.FullName);
                ClosePathPrompt(view);
                if (fromExplorer) FocusSidebar();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                view.ShowError(ex.Message);
            }
        };

        ShowPathPrompt(view);
    }

    private void CutEntry()
    {
        var fromExplorer = ExplorerIsContext;
        if (FileCommandTarget(fromExplorer, "cut") is not { } item) return;
        _workbench.Sidebar.Explorer.Cut(item);
        _workbench.StatusBar.SetMessage(CutMessage(item));
        if (fromExplorer) FocusSidebar();
    }

    private static string CutMessage(IFileSystemInfo item) => $"Cut: {item.FullName}";

    private void PasteEntry()
    {
        var explorer = _workbench.Sidebar.Explorer;
        if (explorer.PendingCut is not { } item) return;
        var fromExplorer = ExplorerIsContext;
        var target = fromExplorer ? explorer.SelectedObject : _workbench.Editor.Group.ActiveTab?.File;
        try
        {
            var moved = _workbench.Move(item, explorer.PastePath(target)!);
            explorer.ClearCut();
            if (!ReferenceEquals(moved, item))
            {
                _history.Rebase(item.FullName, moved.FullName);
                _workbench.StatusBar.SetMessage($"Moved: {moved.FullName}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _workbench.StatusBar.SetMessage(ex.Message);
        }
        if (fromExplorer) FocusSidebar();
    }

    private void ShowPathPrompt(PathPromptView view)
    {
        _activePathPrompt = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusInput();
    }

    private void ClosePathPrompt(PathPromptView view)
    {
        if (!ReferenceEquals(_activePathPrompt, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activePathPrompt = null;
        FocusCallingRegion();
    }

    private void ConfirmDelete()
    {
        if (_activeConfirm is not null) return;
        var fromExplorer = ExplorerIsContext;
        if (FileCommandTarget(fromExplorer, "deleted") is not { } item) return;

        var unsaved = _workbench.Editor.Group.TabsUnder(item.FullName).Count(t => t.IsDirty);
        var message = string.Join('\n', new[]
        {
            item is IDirectoryInfo ? $"Permanently delete '{item.Name}' and its contents?" : $"Permanently delete '{item.Name}'?",
            unsaved switch
            {
                0 => null,
                1 => "1 open file has unsaved changes, which will be lost.",
                _ => $"{unsaved} open files have unsaved changes, which will be lost.",
            },
            "This action is irreversible!",
        }.OfType<string>());

        var view = new ConfirmView("Delete", message, "Delete");
        view.Cancelled += (_, _) =>
        {
            CloseConfirm(view);
            if (fromExplorer) FocusSidebar();
        };
        view.Confirmed += (_, _) =>
        {
            CloseConfirm(view);
            try
            {
                _workbench.Delete(item);
                _history.Forget(item.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _workbench.StatusBar.SetMessage(ex.Message);
            }
            if (fromExplorer) FocusSidebar();
        };

        _activeConfirm = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusCancel();
    }

    private void CloseConfirm(ConfirmView view)
    {
        if (!ReferenceEquals(_activeConfirm, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeConfirm = null;
        FocusCallingRegion();
    }

    private bool ExplorerIsContext => _launchedFromExplorer || _workbench.Sidebar.Explorer.HasFocus;

    // The explorer's selection when it has (or the launcher came from) focus, otherwise the active tab's file.
    private IFileSystemInfo? FileCommandTarget(bool fromExplorer, string verb)
    {
        var explorer = _workbench.Sidebar.Explorer;
        var item = fromExplorer ? explorer.SelectedObject : _workbench.Editor.Group.ActiveTab?.File;
        if (item is null || explorer.Root is null) return null;
        if (!explorer.IsRoot(item)) return item;
        _workbench.StatusBar.SetMessage($"The workspace root can't be {verb}.");
        return null;
    }

    // Show the sidebar and focus its active tab: the explorer tree, or the search query input.
    private void FocusSidebar()
    {
        if (!_workbench.IsSidebarVisible)
            _workbench.SetSidebarVisible(true);
        MoveFocus(_workbench.Sidebar.ActiveTab switch
        {
            SidebarTab.Find => FocusRegion.Find,
            SidebarTab.Review => FocusRegion.Review,
            _ => FocusRegion.Explorer,
        });
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
        FocusCallingRegion();
    }

    private void OpenAbout()
    {
        if (_activeAbout is not null) return;

        var view = new AboutView(AppVersion());
        view.Closed += (_, _) => CloseAbout(view);
        _activeAbout = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.SetFocus();

        view.Present(_sixelSupport);
    }

    private void CloseAbout(AboutView view)
    {
        if (!ReferenceEquals(_activeAbout, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeAbout = null;
        FocusCallingRegion();
    }

    private void OpenDocumentInfo()
    {
        if (_activeDocumentInfo is not null) return;
        if (_workbench.Editor.Group.ActiveTab is not { } tab)
        {
            _workbench.StatusBar.SetMessage("No file is open.");
            return;
        }

        var file = tab.File.FileSystem.FileInfo.New(tab.File.FullName);
        var grammar = tab.Grammar?.Name ?? Workbench.PlainTextName;
        var facts = DocumentInfoView.Facts(grammar, tab.LineEnding, file.Exists ? file.Length : null, tab.IsDirty);
        var view = new DocumentInfoView(WorkspacePath(tab.File), facts, tab.CountDocument(), tab.CountSelection());
        view.Closed += (_, _) => CloseDocumentInfo(view);
        _activeDocumentInfo = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.SetFocus();
    }

    private void CloseDocumentInfo(DocumentInfoView view)
    {
        if (!ReferenceEquals(_activeDocumentInfo, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeDocumentInfo = null;
        FocusCallingRegion();
    }

    private void CompareToSaved()
    {
        var group = _workbench.Editor.Group;
        if ((group.ActiveTab ?? group.ActiveDiffTab?.Source) is not { } tab)
        {
            _workbench.StatusBar.SetMessage("No file is open.");
            return;
        }
        if (!tab.File.FileSystem.File.Exists(tab.File.FullName))
        {
            _workbench.StatusBar.SetMessage($"{tab.File.Name} has never been saved.");
            return;
        }
        if (group.CompareToSaved(tab) is null)
        {
            _workbench.StatusBar.SetMessage("No changes against saved");
            return;
        }
        FocusEditorBody();
    }

    private void GoToChangeLine()
    {
        if (_workbench.Editor.Group.ActiveDiffTab is not { } diff) return;
        // On a draft row (#188) Enter edits the draft, and on a thread row (#186) it reads the thread.
        if (diff.CurrentDraft is { } draft)
        {
            OpenComment(diff, draft.Path, draft.Line, draft);
            return;
        }
        if (diff.ToggleThread()) return;
        if (diff.Source is not { } tab)
        {
            _workbench.StatusBar.SetMessage("Deleted in this branch");
            return;
        }
        var line = diff.CurrentBufferLine;
        _suppressHistory = true;
        try
        {
            _workbench.Editor.Group.OpenOrFocus(tab.File);
            tab.MoveCursor(line, 0);
        }
        finally { _suppressHistory = false; }
        FocusEditorBody();
        _history.Visit(new CursorLocation(tab.File.FullName, line, 0), explicitJump: true);
    }

    /// <summary>
    /// Puts the current change's left-hand lines back into the buffer (#245, #246). Nothing is written
    /// to disk: the tab goes dirty and one Ctrl+Z in the editor takes the whole revert back.
    /// </summary>
    private void RevertChange()
    {
        if (_workbench.Editor.Group.ActiveDiffTab is not { } diff) return;
        if (diff.IsDeleted)
        {
            RestoreDeleted(diff);
            return;
        }
        if (diff.RevertChange() is not { } lines)
        {
            _workbench.StatusBar.SetMessage("No changes");
            return;
        }
        var undo = lines > LargeRevert ? " — Ctrl+Z to undo" : string.Empty;
        _workbench.StatusBar.SetMessage($"Reverted {lines:N0} line{(lines == 1 ? string.Empty : "s")} from {diff.LeftLabel}{undo}");
    }

    /// <summary>
    /// Throws away every change in the focused diff's file at once (#248). It confirms first — unlike a single
    /// revert it drops work that isn't on screen — and is still one buffer edit, with nothing written to disk.
    /// </summary>
    private void RevertAllChanges()
    {
        if (_activeConfirm is not null) return;
        if (_workbench.Editor.Group.ActiveDiffTab is not { } diff) return;
        if (diff.IsDeleted)
        {
            _workbench.StatusBar.SetMessage($"{diff.File.Name} is deleted in this branch — use rc to restore it");
            return;
        }
        var changes = diff.ChangeCount;
        if (changes == 0)
        {
            _workbench.StatusBar.SetMessage("No changes");
            return;
        }

        var view = new ConfirmView("Revert all changes",
            $"Revert all {changes:N0} change{(changes == 1 ? string.Empty : "s")} in {diff.File.Name}?", "Revert");
        view.Cancelled += (_, _) => CloseConfirm(view);
        view.Confirmed += (_, _) =>
        {
            CloseConfirm(view);
            if (diff.RevertAll() is not { } reverted) return;
            _workbench.StatusBar.SetMessage(
                $"Reverted {reverted:N0} change{(reverted == 1 ? string.Empty : "s")} in {diff.File.Name} — Ctrl+Z to undo");
        };

        _activeConfirm = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusCancel();
    }

    /// <summary>
    /// Brings back a file this branch deleted (#247): its base version as an unsaved tab at that path. There's no
    /// buffer to revert into, so this is the file-level version of the same rule — nothing is written until Ctrl+S.
    /// </summary>
    private void RestoreDeleted(DiffTab diff)
    {
        _workbench.Editor.Group.Restore(diff.File, diff.LeftLines);
        FocusEditorBody();
        _workbench.StatusBar.SetMessage($"{diff.File.Name} restored — Ctrl+S to write it back");
    }

    private void CompareToRevision()
    {
        if (_activeRevisionPicker is not null) return;
        var group = _workbench.Editor.Group;
        if ((group.ActiveTab ?? group.ActiveDiffTab?.Source) is not { } tab)
        {
            _workbench.StatusBar.SetMessage("No file is open.");
            return;
        }
        if (!tab.File.FileSystem.File.Exists(tab.File.FullName))
        {
            _workbench.StatusBar.SetMessage($"{tab.File.Name} has never been saved.");
            return;
        }

        var path = tab.File.FullName;
        var root = Task.Run(() => _git.GetRepoRootAsync(path));
        WhenDone(root, () =>
        {
            if (root.Result.Error is { } error)
                _workbench.StatusBar.SetMessage(error);
            else if (root.Result.Value is null)
                _workbench.StatusBar.SetMessage($"{tab.File.Name} isn't in a git repository.");
            else
                OpenRevisionPicker(tab);
        });
    }

    private void CompareToOtherFile()
    {
        if (_activeOpen is not null) return;
        var group = _workbench.Editor.Group;
        if ((group.ActiveTab ?? group.ActiveDiffTab?.Source) is not { } tab)
        {
            _workbench.StatusBar.SetMessage("No file is open.");
            return;
        }
        var start = tab.File.Directory is { Exists: true } folder ? folder : _workbench.Sidebar.Explorer.Root;
        if (start is null) return;

        var view = new OpenView(start, _icons, $"Compare {tab.File.Name} to…", canOpenFolder: false);
        view.Cancelled += (_, _) => CloseOpen(view);
        view.FileSelected += (_, other) =>
        {
            CloseOpen(view);
            CompareTo(tab, other);
        };

        _activeOpen = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusList();
    }

    private void CompareTo(EditorTab tab, IFileInfo other)
    {
        if (string.Equals(other.FullName, tab.File.FullName, StringComparison.Ordinal))
        {
            _workbench.StatusBar.SetMessage($"Can't compare {tab.File.Name} with itself");
            return;
        }
        try
        {
            if (_workbench.Editor.Group.Compare(tab, other.Name, () => DiffTab.ReadLines(other), other.FullName) is null)
                _workbench.StatusBar.SetMessage($"No changes against {other.Name}");
            else
                FocusEditorBody();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _workbench.StatusBar.SetMessage($"Can't read {other.Name}: {ex.Message}");
        }
    }

    private void OpenRevisionPicker(EditorTab tab)
    {
        if (_activeRevisionPicker is not null) return;
        var path = tab.File.FullName;
        var view = new RevisionPickerView(tab.File.Name);
        var lookingUp = false;
        view.Cancelled += (_, _) => CloseRevisionPicker(view);
        view.Submitted += (_, revision) =>
        {
            if (lookingUp) return;
            var group = _workbench.Editor.Group;
            // Close first: removing the picker hands focus back to a tab, switching away from the diff.
            if (group.HasDiff(tab, revision))
            {
                CloseRevisionPicker(view);
                group.FocusDiff(tab, revision);
                FocusEditorBody();
                return;
            }
            lookingUp = true;
            var content = Task.Run(() => ReadRevisionAsync(tab.File.Name, path, revision));
            WhenDone(content, () =>
            {
                lookingUp = false;
                if (!ReferenceEquals(_activeRevisionPicker, view)) return;
                if (content.Result.Error is { } error)
                {
                    view.ShowError(error);
                    return;
                }
                var lines = DiffTab.SplitLines(content.Result.Value);
                CloseRevisionPicker(view);
                if (group.Compare(tab, revision, () => lines) is null)
                    _workbench.StatusBar.SetMessage($"No changes against {revision}");
                else
                    FocusEditorBody();
            });
        };

        _activeRevisionPicker = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusFilter();

        var refs = Task.Run(() => _git.GetRefsAsync(path));
        var history = Task.Run(() => _git.GetFileHistoryAsync(path));
        WhenDone(Task.WhenAll(refs, history), () =>
        {
            if (!ReferenceEquals(_activeRevisionPicker, view)) return;
            view.Load(refs.Result.Value ?? [], history.Result.Value ?? []);
            if ((refs.Result.Error ?? history.Result.Error) is { } error) view.ShowError(error);
        });
    }

    // Scoped commands, not the view's own bindings: the tab header claims Left/Right first (#193).
    private void ScrollDiff(int direction, bool page = false) =>
        _workbench.Editor.Group.ActiveDiffTab?.ScrollSideways(direction, page);

    private void MoveToChange(int direction)
    {
        if (_workbench.Editor.Group.ActiveDiffTab is not { } diff) return;
        if (direction > 0 ? diff.NextChange() : diff.PreviousChange()) return;
        StepReview(diff, direction);
    }

    /// <summary>
    /// Past a review diff's last (or first) change, moves on to the next file the Review tab lists,
    /// skipping any with nothing to show, and closes the diff it leaves (#181). A diff opened any other way stays put.
    /// </summary>
    private void StepReview(DiffTab from, int direction)
    {
        var view = _workbench.Sidebar.Review;
        if (from.Review is not { } spot || view.Review is not { } review || review.MergeBase != spot.Key) return;

        var files = view.ChangedFiles;
        Step(spot.Index + direction);

        void Step(int index)
        {
            if (index < 0 || index >= files.Count)
            {
                _workbench.StatusBar.SetMessage(direction > 0 ? "Last change in the review" : "First change in the review");
                return;
            }

            var at = index;
            ShowReviewDiff(review, files[at], at, files.Count, diff =>
            {
                if (diff is null)
                {
                    Step(at + direction);
                    return;
                }
                if (direction > 0) diff.FirstChange();
                else diff.LastChange();
                if (!ReferenceEquals(diff, from)) _workbench.Editor.Group.CloseDiff(from);
            });
        }
    }

    /// <summary>
    /// PR overview (<c>pro</c>): the Review tab's Overview button from anywhere. The review loads
    /// first when that tab hasn't been shown yet, so the mnemonic works from a cold start.
    /// </summary>
    private void ShowPullRequestOverview()
    {
        var review = _workbench.Sidebar.Review;
        if (review.Review is { PullRequest: not null } loaded)
        {
            OpenOverview(loaded);
            return;
        }
        WhenDone(review.Refresh(), () =>
        {
            if (review.Review is { PullRequest: not null } reloaded) OpenOverview(reloaded);
            else _workbench.StatusBar.SetMessage("No pull request for this branch.");
        });
    }

    /// <summary>
    /// The PR's description and conversation in a read-only Overview tab (#185). It's fetched when the
    /// tab is opened, and whatever gh says instead is shown in the tab, where it can be read in full.
    /// </summary>
    private void OpenOverview(BranchReview review)
    {
        if (review.PullRequest is not { } pullRequest) return;

        var group = _workbench.Editor.Group;
        var fileSystem = _workbench.Sidebar.Explorer.Root?.FileSystem ?? new FileSystem();
        var title = PullRequestOverview.TitleOf(pullRequest.Number);
        var file = fileSystem.FileInfo.New(fileSystem.Path.Combine(review.RepoRoot, title));
        if (group.FocusDocument(file.FullName) is not null)
        {
            FocusEditorBody();
            return;
        }

        _workbench.StatusBar.SetMessage($"Loading #{pullRequest.Number}…");
        var loading = Task.Run(() => _gitHub.GetConversationAsync(review.RepoRoot, pullRequest.Number));
        WhenDone(loading, () =>
        {
            var text = loading.Result.Error ?? PullRequestOverview.Build(loading.Result.Value);
            group.OpenDocument(file, text);
            _workbench.StatusBar.SetMessage(title);
            FocusEditorBody();
        });
    }

    /// <summary>A file's outdated threads (#186) in a document tab, since there's no line left to show them on.</summary>
    private void OpenOutdatedThreads(BranchReview review, ReviewOutdatedNode node)
    {
        if (review.PullRequest is not { } pullRequest) return;

        var group = _workbench.Editor.Group;
        var fileSystem = _workbench.Sidebar.Explorer.Root?.FileSystem ?? new FileSystem();
        var title = PullRequestOverview.OutdatedTitleOf(pullRequest.Number, node.Change.Path);
        // The path only identifies the tab, so it takes neither the separators nor the colon of the title.
        var name = $"#{pullRequest.Number} outdated {node.Change.Path.Replace('/', ' ')}";
        var file = fileSystem.FileInfo.New(fileSystem.Path.Combine(review.RepoRoot, name));
        group.OpenDocument(file, PullRequestOverview.BuildOutdated(pullRequest.Number, node.Change.Path, node.Threads), title);
        FocusEditorBody();
    }

    /// <summary>Threads arrive after the diffs they belong on (#186), so the open ones are given those and their drafts (#188) when they do.</summary>
    private void ShowCommentsInOpenDiffs(BranchReview review)
    {
        OpenDrafts(review);
        var fileSystem = _workbench.Sidebar.Explorer.Root?.FileSystem ?? new FileSystem();
        foreach (var diff in _workbench.Editor.Group.DiffTabs)
        {
            if (diff.Review is not { } spot || spot.Key != review.MergeBase) continue;
            var path = RepoPath(fileSystem, review.RepoRoot, diff.File);
            diff.ShowThreads(review.ThreadsOn(path));
            diff.ShowDrafts(Drafts.On(path));
        }
    }

    /// <summary>The drafts (#188) on the workspace's filesystem, so a mock one keeps a test off the real home directory.</summary>
    private DraftComments Drafts =>
        _draftComments ??= DraftComments.ForUser(_workbench.Sidebar.Explorer.Root?.FileSystem ?? new FileSystem());

    private void OpenDrafts(BranchReview review)
    {
        if (review.PullRequest is { } pullRequest) OpenDrafts(review.RepoRoot, pullRequest.Number);
    }

    /// <summary>Reads back the PR's drafts, and says at the foot of the Review tab how many there are.</summary>
    private void OpenDrafts(string repoRoot, int number)
    {
        Drafts.Open(repoRoot, number);
        _workbench.Sidebar.Review.ShowDraftReview(Drafts.Line);
    }

    private static string RepoPath(IFileSystem fileSystem, string repoRoot, IFileInfo file) =>
        fileSystem.Path.GetRelativePath(repoRoot, file.FullName).Replace('\\', '/');

    private void OpenReviewDiff(BranchReview review, GitChange change)
    {
        var files = _workbench.Sidebar.Review.ChangedFiles;
        var index = 0;
        while (index < files.Count && files[index].Path != change.Path) index++;
        ShowReviewDiff(review, change, index, files.Count, diff =>
        {
            if (diff is null) _workbench.StatusBar.SetMessage($"No changes against {review.Base}");
        });
    }

    /// <summary>
    /// Opens the file and its diff against the review's base, then hands the diff — null when the
    /// buffer matches the base — to <paramref name="done"/>. A read error reports itself and calls nothing.
    /// A deleted file (#182) gets a diff with nothing on the right and no editor tab.
    /// </summary>
    private void ShowReviewDiff(BranchReview review, GitChange change, int index, int count, Action<DiffTab?> done)
    {
        var group = _workbench.Editor.Group;
        var fs = _workbench.Sidebar.Explorer.Root?.FileSystem ?? new FileSystem();
        var file = fs.FileInfo.New(fs.Path.Combine(review.RepoRoot, change.Path));
        var tab = change.Kind == GitChangeKind.Deleted ? null : _workbench.Editor.Open(file);
        var basePath = change.OldPath ?? change.Path;
        var key = $"{review.MergeBase}:{basePath}";
        if ((tab is null ? group.FocusDeletedDiff(file, key) : group.FocusDiff(tab, key)) is { } open)
        {
            Showing(open);
            return;
        }

        var content = change.Kind == GitChangeKind.Added
            ? Task.FromResult(GitResult<string?>.Success(null))
            : Task.Run(() => _git.ShowRepoFileAsync(review.RepoRoot, basePath, review.MergeBase));
        WhenDone(content, () =>
        {
            if (content.Result.Error is { } error)
            {
                _workbench.StatusBar.SetMessage(error);
                return;
            }
            var lines = content.Result.Value is { } text ? DiffTab.SplitLines(text) : [];
            Showing(tab is null
                ? group.CompareDeleted(file, review.Base, () => lines, key)
                : group.Compare(tab, review.Base, () => lines, key));
        });

        void Showing(DiffTab? diff)
        {
            if (diff is not null)
            {
                diff.Review = new ReviewSpot(review.MergeBase, index, count);
                diff.ShowThreads(review.ThreadsOn(change.Path));
                OpenDrafts(review);
                diff.ShowDrafts(Drafts.On(change.Path));
                _workbench.Sidebar.Review.SelectFile(change.Path);
                FocusEditorBody();
            }
            done(diff);
        }
    }

    private async Task<GitResult<string>> ReadRevisionAsync(string name, string path, string revision)
    {
        var resolves = await _git.ResolvesAsync(path, revision);
        if (resolves.Error is { } resolveError) return GitResult<string>.Failure(resolveError);
        if (!resolves.Value) return GitResult<string>.Failure($"No branch, tag or commit called '{revision}'");

        var shown = await _git.ShowFileAsync(path, revision);
        if (shown.Error is { } showError) return GitResult<string>.Failure(showError);
        return shown.Value is { } content
            ? GitResult<string>.Success(content)
            : GitResult<string>.Failure($"{name} isn't in {revision}");
    }

    /// <summary>
    /// Open pull request (<c>opr</c>): the repo's open PRs, listed before the picker opens so a missing
    /// <c>gh</c> is one status-bar line rather than an empty dialog.
    /// </summary>
    private void OpenPullRequest()
    {
        if (_activePullRequestPicker is not null) return;
        if (_workbench.Sidebar.Explorer.Root is not { } root) return;

        var folder = root.FullName;
        _workbench.StatusBar.SetMessage("Loading pull requests…");
        var repoRoot = Task.Run(() => _git.GetRepoRootAsync(folder));
        WhenDone(repoRoot, () =>
        {
            if (repoRoot.Result.Error is { } error)
            {
                _workbench.StatusBar.SetMessage(error);
                return;
            }
            if (repoRoot.Result.Value is not { } repo)
            {
                _workbench.StatusBar.SetMessage($"{folder} isn't in a git repository.");
                return;
            }
            var listing = Task.Run(() => _gitHub.ListPullRequestsAsync(repo));
            WhenDone(listing, () =>
            {
                if (listing.Result.Error is { } listError)
                    _workbench.StatusBar.SetMessage(listError);
                else if (listing.Result.Value.Count == 0)
                    _workbench.StatusBar.SetMessage("No open pull requests.");
                else
                    OpenPullRequestPicker(root, repo, listing.Result.Value);
            });
        });
    }

    private void OpenPullRequestPicker(IDirectoryInfo root, string repoRoot, IReadOnlyList<GitHubPullRequestSummary> pullRequests)
    {
        if (_activePullRequestPicker is not null) return;
        var fileSystem = root.FileSystem;
        var view = new PullRequestPickerView(pullRequests);
        var checkingOut = false;
        view.Cancelled += (_, _) => ClosePullRequestPicker(view);
        view.Submitted += (_, pullRequest) =>
        {
            if (checkingOut) return;
            checkingOut = true;
            view.ShowBusy(pullRequest.Number);

            var checkout = Task.Run(() => PullRequestWorktreeAsync(fileSystem, repoRoot, pullRequest));
            WhenDone(checkout, () =>
            {
                checkingOut = false;
                if (!ReferenceEquals(_activePullRequestPicker, view)) return;
                if (checkout.Result.Error is { } error)
                {
                    view.ShowError(error);
                    return;
                }
                ClosePullRequestPicker(view);
                _workbench.OpenFolder(fileSystem.DirectoryInfo.New(checkout.Result.Value));
                FocusReview();
            });
        };

        _activePullRequestPicker = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusFilter();
    }

    /// <summary>
    /// The worktree to review the PR in: the one that already has its branch checked out, else a new one
    /// beside the repo with the PR checked out in it. gh creates the branch there, so forks work.
    /// </summary>
    private async Task<GitResult<string>> PullRequestWorktreeAsync(IFileSystem fileSystem, string repoRoot, GitHubPullRequestSummary pullRequest)
    {
        if (pullRequest.HeadBranch is { Length: > 0 } branch)
        {
            var existing = await _git.FindWorktreeAsync(repoRoot, branch);
            if (existing.Error is { } findError) return GitResult<string>.Failure(findError);
            if (existing.Value is { } found) return GitResult<string>.Success(found);
        }

        // Next to the repo, as the pitch decided, so the current checkout and its open tabs are left alone.
        var worktreePath = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(repoRoot, "..", $"pr-{pullRequest.Number}"));
        var worktree = await _git.AddWorktreeAsync(repoRoot, worktreePath);
        if (worktree.Error is { } error) return GitResult<string>.Failure(error);

        var checkout = await _gitHub.CheckoutPullRequestAsync(worktreePath, pullRequest.Number);
        return checkout.Error is { } checkoutError
            ? GitResult<string>.Failure(checkoutError)
            : GitResult<string>.Success(worktreePath);
    }

    /// <summary>
    /// Submit review (<c>sr</c>): the branch's PR is looked up before the dialog opens, so no PR and no
    /// <c>gh</c> are both one status-bar line rather than a dialog with nothing to submit to.
    /// </summary>
    private void SubmitReview()
    {
        if (_activeSubmitReview is not null) return;
        if (_workbench.Sidebar.Explorer.Root is not { } root) return;

        var folder = root.FullName;
        const string loading = "Loading pull request…";
        _workbench.StatusBar.SetMessage(loading);
        var repoRoot = Task.Run(() => _git.GetRepoRootAsync(folder));
        WhenDone(repoRoot, () =>
        {
            if (repoRoot.Result.Error is { } error)
            {
                _workbench.StatusBar.SetMessage(error);
                return;
            }
            if (repoRoot.Result.Value is not { } repo)
            {
                _workbench.StatusBar.SetMessage($"{folder} isn't in a git repository.");
                return;
            }
            var found = Task.Run(() => _gitHub.GetPullRequestAsync(repo));
            WhenDone(found, () =>
            {
                if (found.Result.Error is { } lookupError)
                    _workbench.StatusBar.SetMessage(lookupError);
                else if (found.Result.Value is not { } pullRequest)
                    _workbench.StatusBar.SetMessage("No pull request for this branch.");
                else
                {
                    _workbench.StatusBar.ClearMessage(loading);
                    OpenDrafts(repo, pullRequest.Number);
                    OpenSubmitReview(repo, pullRequest.Number);
                }
            });
        });
    }

    private void OpenSubmitReview(string repoRoot, int number)
    {
        if (_activeSubmitReview is not null) return;
        var view = new SubmitReviewView(number, Drafts.Count);
        view.Cancelled += (_, _) => CloseSubmitReview(view);
        view.Submitted += (_, review) =>
        {
            view.ShowBusy();
            var drafts = Drafts.All.ToArray();
            var submitting = Task.Run(() => _gitHub.SubmitReviewAsync(repoRoot, number, review.Verdict, review.Summary, drafts));
            WhenDone(submitting, () =>
            {
                if (!ReferenceEquals(_activeSubmitReview, view)) return;
                if (submitting.Result.Error is { } error)
                {
                    // Nothing is cleared: the drafts are all that's left of a review GitHub refused (#188).
                    view.ShowError(error);
                    return;
                }
                ClearDrafts();
                CloseSubmitReview(view);
                _workbench.StatusBar.SetMessage($"Review submitted on #{number}");
            });
        };

        _activeSubmitReview = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusSummary();
    }

    /// <summary>
    /// Create comment (<c>cc</c>, #188): a draft on the current row of a PR's diff. The file has to match the
    /// PR's head commit, or the comment would land on lines GitHub numbers differently. On a thread it's a
    /// reply instead (#189), which needs no such match — a reply names the thread, not a line.
    /// </summary>
    private void CreateComment()
    {
        if (_activeComment is not null) return;
        var tab = _workbench.Sidebar.Review;
        if (tab.ListHasFocus && tab.SelectedThread is { } outdated)
        {
            OpenReply(outdated, fromReviewTab: true);
            return;
        }
        if (_workbench.Editor.Group.ActiveDiffTab is { CurrentThread: { } onRow })
        {
            OpenReply(onRow, fromReviewTab: false);
            return;
        }
        if (_workbench.Editor.Group.ActiveDiffTab is not { Review: { } spot } diff
            || _workbench.Sidebar.Review.Review is not { PullRequest: { } pullRequest } review
            || spot.Key != review.MergeBase)
        {
            _workbench.StatusBar.SetMessage("Open a file from the Review tab to comment");
            return;
        }
        if (diff.CurrentHeadLine is not { } line)
        {
            _workbench.StatusBar.SetMessage("Comment on a line on the right");
            return;
        }

        var fileSystem = _workbench.Sidebar.Explorer.Root?.FileSystem ?? new FileSystem();
        var path = RepoPath(fileSystem, review.RepoRoot, diff.File);
        var head = Task.Run(() => _git.ShowRepoFileAsync(review.RepoRoot, path, pullRequest.HeadSha));
        WhenDone(head, () =>
        {
            if (head.Result.Error is { } error)
            {
                _workbench.StatusBar.SetMessage(error);
                return;
            }
            if (!MatchesHead(head.Result.Value, diff))
            {
                _workbench.StatusBar.SetMessage(
                    $"This file differs from #{pullRequest.Number}'s head: comments would land on the wrong lines");
                return;
            }
            OpenDrafts(review);
            OpenComment(diff, path, line, null);
        });
    }

    /// <summary>
    /// Replies to <paramref name="thread"/> (#189). GitHub can't hold a reply in a pending review, so it's
    /// posted as soon as the dialog is confirmed, and whatever it says back keeps the dialog and its text.
    /// </summary>
    private void OpenReply(GitHubReviewThread thread, bool fromReviewTab)
    {
        if (_workbench.Sidebar.Review.Review is not { PullRequest: { } pullRequest } review) return;
        if (thread.ReplyToId == 0)
        {
            _workbench.StatusBar.SetMessage("GitHub didn't say what to reply to on this thread");
            return;
        }

        var view = new CommentView(thread);
        view.Cancelled += (_, _) => CloseComment(view, fromReviewTab);
        view.Added += (_, body) =>
        {
            view.ShowBusy();
            var posting = Task.Run(() => _gitHub.ReplyToThreadAsync(review.RepoRoot, pullRequest.Number, thread.ReplyToId, body));
            WhenDone(posting, () =>
            {
                if (!ReferenceEquals(_activeComment, view)) return;
                if (posting.Result.Error is { } error)
                {
                    view.ShowError(error);
                    return;
                }
                // Closed first: rebuilding the Review tab under an open modal drops TG's focus, and the
                // editor's Tabs then makes whichever tab takes it active (#191).
                CloseComment(view, fromReviewTab);
                ShowReply(thread, posting.Result.Value);
                _workbench.StatusBar.SetMessage($"Replied on #{pullRequest.Number}");
            });
        };

        _activeComment = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusBody();
    }

    /// <summary>Puts a posted reply on its thread wherever the thread is shown, rather than refetching them all.</summary>
    private void ShowReply(GitHubReviewThread thread, GitHubComment reply)
    {
        var updated = thread with { Comments = [.. thread.Comments, reply] };
        foreach (var diff in _workbench.Editor.Group.DiffTabs) diff.ReplaceThread(thread, updated);
        _workbench.Sidebar.Review.ReplaceThread(thread, updated);
    }

    /// <summary>Whether the buffer being commented on is line for line what GitHub has at the PR's head.</summary>
    private static bool MatchesHead(string? content, DiffTab diff) =>
        content is { } text && diff.Source is { } source && DiffTab.SplitLines(text).SequenceEqual(source.Lines);

    private void OpenComment(DiffTab diff, string path, int line, DraftComment? draft)
    {
        if (_activeComment is not null) return;
        var view = new CommentView(path, line, draft);
        view.Cancelled += (_, _) => CloseComment(view);
        view.Added += (_, body) =>
        {
            if (draft is null) Drafts.Add(path, line, body);
            else Drafts.Replace(draft, body);
            CloseComment(view);
            ShowDrafts(diff, path);
        };
        view.Deleted += (_, _) =>
        {
            if (draft is not null) Drafts.Remove(draft);
            CloseComment(view);
            ShowDrafts(diff, path);
        };

        _activeComment = view;
        _workbench.Add(view);
        _scopes.Push(view.Scope);
        view.FocusBody();
    }

    /// <summary>Once a review is posted its drafts are GitHub's; they go from the diffs and the Review tab too.</summary>
    private void ClearDrafts()
    {
        Drafts.Clear();
        foreach (var diff in _workbench.Editor.Group.DiffTabs)
            if (diff.Drafts.Count > 0)
                diff.ShowDrafts([]);
        _workbench.Sidebar.Review.ShowDraftReview(Drafts.Line);
    }

    private void ShowDrafts(DiffTab diff, string path)
    {
        diff.ShowDrafts(Drafts.On(path));
        _workbench.Sidebar.Review.ShowDraftReview(Drafts.Line);
    }

    private void CloseComment(CommentView view, bool fromReviewTab = false)
    {
        if (!ReferenceEquals(_activeComment, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeComment = null;
        if (fromReviewTab) MoveFocus(FocusRegion.Review);
        else FocusEditorBody();
    }

    private void CloseSubmitReview(SubmitReviewView view)
    {
        if (!ReferenceEquals(_activeSubmitReview, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeSubmitReview = null;
        FocusCallingRegion();
    }

    private void ClosePullRequestPicker(PullRequestPickerView view)
    {
        if (!ReferenceEquals(_activePullRequestPicker, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activePullRequestPicker = null;
        FocusCallingRegion();
    }

    private void CloseRevisionPicker(RevisionPickerView view)
    {
        if (!ReferenceEquals(_activeRevisionPicker, view)) return;
        _scopes.Pop(view.Scope);
        _workbench.Remove(view);
        view.Dispose();
        _activeRevisionPicker = null;
        FocusCallingRegion();
    }

    /// <summary>Runs <paramref name="then"/> on the UI thread once <paramref name="task"/> has succeeded.</summary>
    private void WhenDone(Task task, Action then) =>
        task.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) _app.Invoke(then);
        }, TaskScheduler.Default);

    private string WorkspacePath(IFileInfo file)
    {
        var explorer = _workbench.Sidebar.Explorer;
        if (explorer.Root is null) return file.FullName;
        var relative = explorer.RelativePath(file);
        return relative.StartsWith("..", StringComparison.Ordinal) ? file.FullName : relative;
    }

    private static string AppVersion() =>
        VersionText(Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);

    /// <summary>The version About shows: MinVer's, without the commit it appends (#214).</summary>
    internal static string VersionText(string? informationalVersion) =>
        informationalVersion?.Split('+')[0] ?? "unknown";

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
        FocusCallingRegion();
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
        _app.Mouse.MouseEvent -= OnAppMouseEvent;
        _app.Iteration -= OnIteration;
        _keybindings.ChordChanged -= OnChordChanged;
        _workbench.Editor.Group.CursorMoved -= OnEditorCursorMoved;
        _workbench.Editor.Group.ActiveTabChanged -= OnActiveTabChanged;
        _find.Dispose();
        _terminalCursors.Dispose();
        _workbench.Dispose();
        _app.Dispose();
        // Tell WezTerm the tuicode key table should be popped; matches the startup activation.
        // Emitted post-Dispose so it reaches the live terminal after TG restores it.
        WriteToTerminal("\x1b]1337;SetUserVar=TUICODE_ACTIVE=MA==\x07");
        // OSC 112 restores the terminal's own cursor colour.
        WriteToTerminal("\x1b]112\x07");
        _flowControl.Dispose();
    }
}
