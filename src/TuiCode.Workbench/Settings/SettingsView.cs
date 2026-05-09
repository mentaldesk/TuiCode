using TuiCode.Abstractions;
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
    private static readonly string[] CategoryNames = ["Theme", "Keyboard Shortcuts"];

    private readonly ISettingsService _settings;
    private readonly Action<IEnumerable<KeyBinding>> _applyEditedBindings;
    private readonly string _originalTheme;

    private readonly ListView _categoriesList;
    private readonly View _separator;

    private readonly ThemePickerView _themePicker;
    private readonly KeybindingsPickerView _keybindingsPicker;

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
        Action<IEnumerable<KeyBinding>> applyEditedBindings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workbenchKeybindings);
        ArgumentNullException.ThrowIfNull(workbenchCommands);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(applyEditedBindings);

        _settings = settings;
        _applyEditedBindings = applyEditedBindings;
        _originalTheme = settings.Theme;

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
            Source = new ListWrapper<string>(new(CategoryNames))
        };
        _categoriesList.SelectedItem = 0;
        _categoriesList.ValueChanged += (_, _) => SwapPanel();
        _categoriesList.KeyDown += OnCategoriesKey;

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

        _keybindingsPicker = new KeybindingsPickerView(workbenchCommands, workbenchKeybindings, scopes)
        {
            X = Pos.Right(_separator) + 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Visible = false
        };

        var footer = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Ctrl+Enter: Save   Esc: Cancel   Ctrl+0 / Ctrl+Esc: Categories"
        };

        Add(_categoriesList, _separator, _themePicker, _keybindingsPicker, footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();

        _categoriesList.SetFocus();
    }

    private void SwapPanel()
    {
        var i = _categoriesList.SelectedItem ?? 0;
        var showKb = i == 1;
        _themePicker.Visible = !showKb;
        _keybindingsPicker.Visible = showKb;
    }

    private void OnCategoriesKey(object? sender, Key key)
    {
        if (key == Key.CursorRight || key == Key.Enter || key == Key.Space)
        {
            FocusActivePanel();
            key.Handled = true;
        }
    }

    /// <summary>Public so panels can call back to return focus to the categories list (e.g. on Left arrow).</summary>
    public void FocusCategories() => _categoriesList.SetFocus();

    private void FocusActivePanel()
    {
        if (_keybindingsPicker.Visible) _keybindingsPicker.FocusContent();
        else _themePicker.FocusContent();
    }

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
        _settings.Save();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        if (!string.Equals(_settings.Theme, _originalTheme, StringComparison.Ordinal))
            _settings.Theme = _originalTheme;
        // Pending keybinding edits are dropped — they were never applied to the live trie.
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
