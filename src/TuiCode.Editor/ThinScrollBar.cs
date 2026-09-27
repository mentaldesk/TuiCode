using System.Text;

namespace TuiCode.Editor;

/// <summary>Draws a horizontal bar half a row high, so it looks as thick as the vertical one (#296).</summary>
internal static class ThinScrollBar
{
    private static readonly Rune LowerHalf = new('▄');

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
            slider.SetAttributeForRole(VisualRole.Normal);
            slider.FillRect(slider.Viewport with { Width = slider.Size }, LowerHalf);
        };
    }
}
