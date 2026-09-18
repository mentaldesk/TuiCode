using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Icons;

public static class IconDrawing
{
    public static Attribute AttributeFor(FileIcon icon, Attribute row)
    {
        var dark = row.Background == Color.None || row.Background.IsDarkColor();
        return icon.ColorFor(dark) is { } rgb
            ? new Attribute(new Color((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff), row.Background, row.Style)
            : row;
    }

    // At draw time rather than in AspectGetter, so the tree's type-to-jump still matches on the name.
    public static void Prepend<T>(DrawTreeViewLineEventArgs<T> e, FileIcon icon) where T : class
    {
        // Negative when scrolled horizontally past the start of the text.
        var at = e.IndexOfModelText;
        if (e.Cells is not { } cells || at < 0 || at >= cells.Count) return;

        var row = cells[at].Attribute ?? default;
        cells.InsertRange(at,
        [
            new Cell { Grapheme = icon.Glyph, Attribute = AttributeFor(icon, row) },
            new Cell { Grapheme = " ", Attribute = row },
        ]);
    }
}
