namespace TuiCode.Editor;

/// <summary>
/// Where an explicit jump leaves the viewport (#200): the whole range it targets in view with a margin
/// above and below it, or no scroll at all when it's already comfortably there.
/// </summary>
public static class Reveal
{
    /// <summary>Rows kept above a revealed range's first line and below its last, in both panes.</summary>
    public const int Margin = 2;

    /// <summary>
    /// The row to scroll a <paramref name="height"/>-row viewport currently at <paramref name="top"/> to,
    /// so that lines <paramref name="first"/> to <paramref name="last"/> of a <paramref name="lineCount"/>-line
    /// file are in view with <see cref="Margin"/> rows to spare at each end; null to leave it where it is,
    /// which includes a view that hasn't been laid out yet.
    /// </summary>
    public static int? TopRow(int top, int height, int lineCount, int first, int last)
    {
        if (height <= 0) return null;
        if (first - top >= Margin && top + height - 1 - last >= Margin) return null;

        // A viewport shorter than the margin would otherwise push the line we're revealing off the bottom.
        var margin = Math.Min(Margin, height - 1);
        return Math.Clamp(first - margin, 0, Math.Max(lineCount - height, 0));
    }
}
