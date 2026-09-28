using System.Text;

namespace TuiCode.Editor;

/// <summary>Draws a horizontal bar as a row of domino tiles, so it looks as thick as the vertical one (#296).</summary>
internal static class ThinScrollBar
{
    public const string Tile = "🀰";

    private static readonly Rune TileRune = Rune.GetRuneAt(Tile, 0);

    public static void Apply(ScrollBar bar)
    {
        bar.DrawingContent += (_, _) =>
        {
            if (bar.Orientation != Orientation.Horizontal || !bar.Slider.Visible) return;
            bar.SetAttributeForRole(VisualRole.Normal);
            bar.FillRect(bar.Viewport with { X = bar.Viewport.X + 1, Width = bar.Viewport.Width - 2 }, new Rune(' '));
        };
        bar.Slider.DrawingContent += (_, _) =>
        {
            if (bar.Orientation != Orientation.Horizontal) return;
            var slider = bar.Slider;
            var tiles = slider.Viewport with { Width = slider.Size };
            if (slider.Frame.X <= 1) tiles = tiles with { X = tiles.X + 1, Width = tiles.Width - 1 };
            if (slider.Frame.X + slider.Size >= bar.Viewport.Width - 1) tiles = tiles with { Width = tiles.Width - 1 };
            slider.SetAttributeForRole(VisualRole.Normal);
            slider.FillRect(slider.Viewport with { Width = slider.Size }, new Rune(' '));
            slider.FillRect(tiles, TileRune);
        };
    }
}
