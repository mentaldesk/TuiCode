using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>Settings → Interface (#209). <see cref="SettingsView"/> applies <see cref="Current"/> on save.</summary>
public sealed class InterfaceSettingsView : View
{
    private const int ControlColumn = 15;

    private readonly NumericUpDown<int> _sidebarWidth;

    public int Current => _sidebarWidth.Value;

    public InterfaceSettingsView(int sidebarWidth)
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        // Required: TG only allows focus on a descendant if every ancestor has CanFocus = true.
        CanFocus = true;

        var caption = new Label { X = 0, Y = 0, Text = "Sidebar width" };
        _sidebarWidth = new NumericUpDown<int> { X = ControlColumn, Y = 0, Value = sidebarWidth };
        _sidebarWidth.ValueChanging += (_, e) =>
            e.Handled = e.NewValue is < SidebarSizing.Min or > SidebarSizing.SettingsMax;
        var units = new Label { X = Pos.Right(_sidebarWidth) + 1, Y = 0, Text = "columns" };

        Add(caption, _sidebarWidth, units);
        KeyDown += OnKey;
    }

    public bool FocusContent() => _sidebarWidth.SetFocus();

    private void OnKey(object? sender, Key key)
    {
        if (key == Key.CursorLeft && SuperView is SettingsView settings)
        {
            settings.FocusCategories();
            key.Handled = true;
        }
    }
}
