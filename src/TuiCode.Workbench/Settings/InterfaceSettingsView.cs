using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>Settings → Interface (#209). <see cref="SettingsView"/> applies <see cref="Current"/> on save.</summary>
public sealed class InterfaceSettingsView : View
{
    private const int ControlColumn = 15;

    private readonly NumericUpDown<int> _sidebarWidth;
    private readonly CheckBox _altOpensMenu;

    public int Current => _sidebarWidth.Value;

    public bool AltOpensMenuWhileTyping => _altOpensMenu.Value == CheckState.Checked;

    public InterfaceSettingsView(int sidebarWidth, bool altOpensMenuWhileTyping = true)
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        // Required: TG only allows focus on a descendant if every ancestor has CanFocus = true.
        CanFocus = true;

        var caption = new VerbatimLabel { X = 0, Y = 0, Text = "Sidebar width" };
        _sidebarWidth = new NumericUpDown<int> { X = ControlColumn, Y = 0, Value = sidebarWidth };
        _sidebarWidth.ValueChanging += (_, e) =>
            e.Handled = e.NewValue is < SidebarSizing.Min or > SidebarSizing.SettingsMax;
        var units = new VerbatimLabel { X = Pos.Right(_sidebarWidth) + 1, Y = 0, Text = "columns" };

        _altOpensMenu = new CheckBox
        {
            X = 0,
            Y = 2,
            Text = "Alt + a menu letter opens the menu while typing",
            Value = altOpensMenuWhileTyping ? CheckState.Checked : CheckState.UnChecked,
        };

        Add(caption, _sidebarWidth, units, _altOpensMenu);
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
