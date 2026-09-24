using TuiCode.Abstractions;
using TuiCode.Icons;
using TuiCode.Syntax;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Modal full-screen settings overlay. Owns its own command + keybinding services so it doesn't
/// pollute the workbench scope. The owning <see cref="WorkbenchHost"/> pushes <see cref="Scope"/>
/// onto the input scope stack when showing and pops it on <see cref="Closed"/>.
///
/// Categories are listed on the left, the corresponding panel renders on the right.
/// </summary>
public sealed class SettingsView : Window
{
    private readonly ISettingsService _settings;
    private readonly Action<IEnumerable<KeyBinding>> _applyEditedBindings;
    private readonly Action? _applyGrammarAssociations;
    private readonly string _originalTheme;
    private readonly FileIcons? _icons;
    private readonly FileIconStyle _originalIcons;

    private readonly ListView _categoriesList;
    private readonly View _separator;

    private readonly ThemePickerView _themePicker;
    private readonly EditorSettingsView _editorSettings;
    private readonly KeybindingsPickerView _keybindingsPicker;
    private readonly GrammarAssociationsView _grammarAssociations;
    private readonly TerminalIntegrationPickerView _terminalIntegrationPicker;
    private readonly InterfaceSettingsView _interfaceSettings;
    private readonly List<(string Name, View Panel, Func<bool> Focus)> _panels;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    /// <summary>Fired after the view has finished its save-or-cancel and wants to be removed.</summary>
    public event EventHandler? Closed;

    public SettingsView(
        ISettingsService settings,
        IKeybindingService workbenchKeybindings,
        ICommandService workbenchCommands,
        IInputScopeStack scopes,
        Action<IEnumerable<KeyBinding>> applyEditedBindings,
        IEnumerable<ITerminalIntegration> terminalIntegrations,
        IEnvironment environment,
        SyntaxHighlighter? syntax = null,
        Action? applyGrammarAssociations = null,
        FileIcons? icons = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workbenchKeybindings);
        ArgumentNullException.ThrowIfNull(workbenchCommands);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(applyEditedBindings);
        ArgumentNullException.ThrowIfNull(terminalIntegrations);
        ArgumentNullException.ThrowIfNull(environment);

        _settings = settings;
        _applyEditedBindings = applyEditedBindings;
        _applyGrammarAssociations = applyGrammarAssociations;
        _originalTheme = settings.Theme;
        _icons = icons;
        _originalIcons = icons?.Setting ?? settings.FileIcons;

        Title = "Settings";
        BorderStyle = LineStyle.Single;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill(1); // leave the status bar visible

        _categoriesList = new ListView
        {
            X = 1,
            Y = 1,
            Width = 22,
            Height = Dim.Fill(2),
        };
        _categoriesList.ValueChanged += (_, _) => SwapPanel();
        // KeyDown on the categories ListView itself doesn't fire reliably in this layout —
        // TG seems to route keys to the focused Window (us) rather than the inner ListView,
        // even though the list visibly responds to Up/Down. So intercept at the Window level
        // (KeyDown below) and gate on HasFocus to avoid trampling search-field typing.
        KeyDown += OnSettingsKey;

        _separator = new Label
        {
            X = Pos.Right(_categoriesList) + 1,
            Y = 1,
            Width = 1,
            Height = Dim.Fill(2),
            Text = "│"
        };

        _themePicker = new ThemePickerView(settings)
        {
            X = Pos.Right(_separator) + 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Visible = true
        };

        _editorSettings = new EditorSettingsView(settings.Editor)
        {
            X = Pos.Right(_separator) + 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Visible = false
        };

        _keybindingsPicker = new KeybindingsPickerView(workbenchCommands, workbenchKeybindings, scopes)
        {
            X = Pos.Right(_separator) + 1,
            Y = 1,
            // One cell wider than the other panels, so the longest default chord fits beside its command at 80 columns.
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            Visible = false
        };

        _grammarAssociations = new GrammarAssociationsView(syntax, settings.GrammarAssociations, scopes)
        {
            X = Pos.Right(_separator) + 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Visible = false
        };

        _terminalIntegrationPicker = new TerminalIntegrationPickerView(terminalIntegrations, environment)
        {
            X = Pos.Right(_separator) + 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Visible = false
        };

        _interfaceSettings = new InterfaceSettingsView(settings.SidebarWidth)
        {
            X = Pos.Right(_separator) + 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Visible = false
        };

        _panels =
        [
            ("Theme", _themePicker, _themePicker.FocusContent),
            ("Editor", _editorSettings, _editorSettings.FocusContent),
            ("Keyboard Shortcuts", _keybindingsPicker, _keybindingsPicker.FocusContent),
            ("Grammars", _grammarAssociations, _grammarAssociations.FocusContent),
            ("Terminal Integration", _terminalIntegrationPicker, _terminalIntegrationPicker.FocusContent),
            ("Interface", _interfaceSettings, _interfaceSettings.FocusContent),
        ];
        if (icons is not null)
        {
            var iconsPicker = new FileIconsPickerView(icons)
            {
                X = Pos.Right(_separator) + 1,
                Y = 1,
                Width = Dim.Fill(2),
                Height = Dim.Fill(2),
                Visible = false
            };
            _panels.Insert(1, ("File Icons", iconsPicker, iconsPicker.FocusContent));
        }
        _categoriesList.Source = new ListWrapper<string>(new(_panels.Select(p => p.Name)));
        _categoriesList.SelectedItem = 0;

        var footer = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Ctrl+Enter: Save   Esc: Cancel   Ctrl+0 / Ctrl+Esc: Categories"
        };

        Add(_categoriesList, _separator);
        foreach (var panel in _panels)
            Add(panel.Panel);
        Add(footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();

        _categoriesList.SetFocus();
    }

    private void SwapPanel()
    {
        var selected = _categoriesList.SelectedItem ?? 0;
        for (var i = 0; i < _panels.Count; i++)
            _panels[i].Panel.Visible = i == selected;
    }

    private void OnSettingsKey(object? sender, Key key)
    {
        // Drill from categories into the active panel. Only fires while categories has focus
        // (or while the modal Window itself does, which is the post-open state) — so search
        // field typing in the picker isn't trampled.
        var categoriesActive = _categoriesList.HasFocus || (HasFocus && !PanelHasFocus());
        if (categoriesActive && (key == Key.CursorRight || key == Key.Enter || key == Key.Space))
        {
            FocusActivePanel();
            key.Handled = true;
        }
    }

    private bool PanelHasFocus() => _panels.Any(p => p.Panel.Visible && HasFocusDescendant(p.Panel));

    private static bool HasFocusDescendant(View v)
    {
        if (v.HasFocus) return true;
        foreach (var child in v.SubViews)
            if (HasFocusDescendant(child)) return true;
        return false;
    }

    /// <summary>Public so panels can call back to return focus to the categories list (e.g. on Left arrow).</summary>
    public void FocusCategories() => _categoriesList.SetFocus();

    private bool FocusActivePanel() => _panels.FirstOrDefault(p => p.Panel.Visible).Focus?.Invoke() ?? false;

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.SettingsSave, Save);
        _scopeCommands.Register(CommandIds.SettingsCancel, Cancel);
        _scopeCommands.Register(CommandIds.SettingsFocusCategories, () => _categoriesList.SetFocus());

        _scopeKeybindings.Bind("Ctrl+Enter", CommandIds.SettingsSave);
        _scopeKeybindings.Bind("Esc", CommandIds.SettingsCancel);
        _scopeKeybindings.Bind("Ctrl+D0", CommandIds.SettingsFocusCategories);
        _scopeKeybindings.Bind("Ctrl+Esc", CommandIds.SettingsFocusCategories);
    }

    private void Save()
    {
        _applyEditedBindings(_keybindingsPicker.CurrentBindings);
        _settings.SetGrammarAssociations(_grammarAssociations.CurrentAssociations);
        if (_icons is not null)
            _settings.FileIcons = _icons.Setting;
        _settings.Editor = _editorSettings.Current;
        _settings.SidebarWidth = _interfaceSettings.Current;
        _settings.Save();
        _applyGrammarAssociations?.Invoke();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        if (!string.Equals(_settings.Theme, _originalTheme, StringComparison.Ordinal))
            _settings.Theme = _originalTheme;
        _icons?.Setting = _originalIcons;
        // Pending keybinding edits are dropped — they were never applied to the live trie.
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
