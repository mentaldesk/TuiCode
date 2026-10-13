using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Search;

/// <summary>
/// A find box's option. Not focusable, so Tab still goes straight between the fields. Its letter is underlined
/// as a menu item's is, but isn't a hot key: Alt+C and Alt+W are workbench commands.
/// </summary>
public sealed class FindOption : CheckBox
{
    public FindOption(string text)
    {
        Text = text;
        HotKey = Key.Empty;
        CanFocus = false;
    }

    protected override bool OnGettingAttributeForRole(in VisualRole role, ref Attribute currentAttribute)
    {
        if (role is not (VisualRole.HotNormal or VisualRole.HotFocus))
            return base.OnGettingAttributeForRole(role, ref currentAttribute);
        currentAttribute = currentAttribute with { Style = currentAttribute.Style | TextStyle.Underline };
        return true;
    }
}
