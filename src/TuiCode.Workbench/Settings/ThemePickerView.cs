using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Right-pane panel for the "Theme" category. Live preview: setting an item updates the
/// theme immediately via <see cref="ISettingsService.Theme"/>. The owning <see cref="SettingsView"/>
/// captures the original theme on open and restores it on cancel.
/// </summary>
public sealed class ThemePickerView : View
{
    private readonly ListView _list;

    public ThemePickerView(ISettingsService settings)
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var themes = settings.AvailableThemes.ToArray();
        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = new ListWrapper<string>(new(themes))
        };
        var initialIndex = Array.IndexOf(themes, settings.Theme);
        if (initialIndex >= 0) _list.SelectedItem = initialIndex;
        _list.ValueChanged += (_, _) =>
        {
            var i = _list.SelectedItem ?? -1;
            if (i < 0 || i >= themes.Length) return;
            settings.Theme = themes[i];
        };

        Add(_list);
    }

    public bool FocusContent() => _list.SetFocus();
}
