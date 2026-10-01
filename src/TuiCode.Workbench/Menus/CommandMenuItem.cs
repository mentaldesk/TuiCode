using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Workbench.Menus;

/// <summary>
/// A menu item that can be dimmed: drawn in the theme's disabled colour and inert, but still enabled so the
/// arrow keys stop on it, where TG skips a disabled view (#342).
/// </summary>
public sealed class CommandMenuItem : MenuItem
{
    public bool Dimmed
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            SetNeedsDraw();
        }
    }

    protected override bool OnGettingAttributeForRole(in VisualRole role, ref Attribute currentAttribute)
    {
        if (!Dimmed) return base.OnGettingAttributeForRole(role, ref currentAttribute);
        var scheme = GetScheme();
        var disabled = scheme.GetAttributeForRole(VisualRole.Disabled, App?.Driver?.DefaultAttribute);
        currentAttribute = HasFocus
            ? disabled with { Background = scheme.GetAttributeForRole(VisualRole.Focus, App?.Driver?.DefaultAttribute).Background }
            : disabled;
        return true;
    }

    protected override bool OnActivating(CommandEventArgs args) => Dimmed || base.OnActivating(args);

    protected override bool OnAccepting(CommandEventArgs args) => Dimmed || base.OnAccepting(args);
}
