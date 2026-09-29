namespace TuiCode.Editor;

/// <summary>Keeps a view's place when its scroll bar is resized (#324).</summary>
internal static class ScrollBarResize
{
    // TG 2.1.0 re-clamps a resized bar's slider and reads the lossy result back as a new scroll position.
    public static void Hold(ScrollBar bar, Func<int> scrolledTo)
    {
        var frame = bar.Frame;
        bar.FrameChanged += (_, e) => frame = e.Value;
        bar.ValueChanging += (_, e) => e.Handled = bar.Frame != frame && e.NewValue != scrolledTo();
    }
}
