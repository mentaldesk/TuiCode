using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Modal full-screen settings overlay. Owns its own command + keybinding services so
/// it doesn't pollute the workbench scope. Push the <see cref="Scope"/> onto the input
/// scope stack when showing; pop it on <see cref="Closed"/>.
/// </summary>
public sealed class SettingsView : Window
{
    private static readonly string[] Categories = ["Theme"];

    private readonly ISettingsService _settings;
    private readonly string _originalTheme;
    private readonly ListView _categoriesList;
    private readonly ListView _themesList;
    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    /// <summary>Fired after the view has finished its save-or-cancel and wants to be removed.</summary>
    public event EventHandler? Closed;

    public SettingsView(ISettingsService settings)
    {
        _settings = settings;
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
            Width = 20,
            Height = Dim.Fill(2),
            Source = new ListWrapper<string>(new(Categories))
        };

        var separator = new Label
        {
            X = Pos.Right(_categoriesList) + 1,
            Y = 1,
            Width = 1,
            Height = Dim.Fill(2),
            Text = "│"
        };

        var themes = settings.AvailableThemes.ToArray();
        _themesList = new ListView
        {
            X = Pos.Right(separator) + 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Source = new ListWrapper<string>(new(themes))
        };
        var initialIndex = Array.IndexOf(themes, _originalTheme);
        if (initialIndex >= 0) _themesList.SelectedItem = initialIndex;
        _themesList.ValueChanged += (_, _) =>
        {
            var i = _themesList.SelectedItem ?? -1;
            if (i < 0 || i >= themes.Length) return;
            settings.Theme = themes[i];
        };

        var footer = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Ctrl+Enter: Save   Esc: Cancel   Ctrl+0 / Ctrl+Esc: Categories"
        };

        Add(_categoriesList, separator, _themesList, footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();

        _categoriesList.SetFocus();
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
        _settings.Save();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        if (!string.Equals(_settings.Theme, _originalTheme, StringComparison.Ordinal))
            _settings.Theme = _originalTheme;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
