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
    /// file are in view with <see cref="Margin"/> rows to spare at each end, below the rows
    /// <paramref name="pinnedAt"/> says are pinned over a given top (#474); null to leave it where it is,
    /// which includes a view that hasn't been laid out yet.
    /// </summary>
    public static int? TopRow(int top, int height, int lineCount, int first, int last, Func<int, int>? pinnedAt = null)
    {
        if (height <= 0) return null;
        pinnedAt ??= _ => 0;
        if (first - top - pinnedAt(top) >= Margin && top + height - 1 - last >= Margin) return null;

        // A viewport shorter than the margin would otherwise push the line we're revealing off the bottom.
        var margin = Math.Min(Margin, height - 1);
        return Below(first, margin, Math.Max(lineCount - height, 0), pinnedAt);
    }

    /// <summary>The lowest top, up to <paramref name="maxTop"/>, that leaves <paramref name="row"/> <paramref name="margin"/> rows below whatever is pinned.</summary>
    public static int Below(int row, int margin, int maxTop, Func<int, int> pinnedAt)
    {
        var top = Math.Clamp(row - margin, 0, maxTop);
        for (var pinned = 1; pinned <= StickyLines.Max && pinnedAt(top) > row - margin - top; pinned++)
            top = Math.Clamp(row - margin - pinned, 0, maxTop);
        return top;
    }
}
