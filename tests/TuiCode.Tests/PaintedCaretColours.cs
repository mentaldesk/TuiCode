using MentalDesk.Tui.Theming;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Terminal.Gui.ViewBase;

namespace TuiCode.Tests;

/// <summary>The colours a text box paints its caret in: the theme's Cursor scheme, inverted.</summary>
internal static class PaintedCaretColours
{
    public static Attribute Of(View field)
    {
        var colours = SchemeManager.TryGetScheme(SchemeNames.Cursor, out var cursor)
            ? cursor.Normal
            : field.GetAttributeForRole(VisualRole.Editable);
        return new Attribute(colours.Background, colours.Foreground);
    }
}
