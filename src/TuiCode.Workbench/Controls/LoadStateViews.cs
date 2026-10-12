using MentalDesk.Tui.Loading;
using TuiCode.Workbench.Review;

namespace TuiCode.Workbench.Controls;

internal static class LoadStateViews
{
    /// <summary>
    /// Shows a failed load, cut to one row since the message isn't wrapped, and with <c>R retry</c> only when
    /// <paramref name="retry"/> could help: outside a git repository it couldn't, so that's said without it.
    /// </summary>
    public static void ShowLoadFailed(this LoadStateView view, string message, Action? retry)
    {
        var shown = BranchReview.Truncate(message, view.Viewport.Width);
        if (retry is null) view.ShowEmpty(shown);
        else view.ShowFailed(shown, ("R retry", retry));
    }
}
