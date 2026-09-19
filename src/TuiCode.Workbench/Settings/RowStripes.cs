using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Workbench.Settings;

internal static class RowStripes
{
    private const double Tint = 0.1;

    // Null leaves the row to the list, so the selected row keeps its highlight.
    public static Attribute? For(int row, int? selectedRow, Attribute normal) =>
        row % 2 == 1 && row != selectedRow ? Stripe(normal) : null;

    public static Attribute Stripe(Attribute normal) =>
        normal with { Background = Blend(normal.Background, normal.Foreground) };

    private static Color Blend(Color from, Color to) =>
        new(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));

    private static int Mix(byte from, byte to) => (int)Math.Round(from + (to - from) * Tint);
}
