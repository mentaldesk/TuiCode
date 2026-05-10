using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class NavigationHistoryServiceTests
{
    [Fact]
    public void Empty_history_cannot_go_back_or_forward()
    {
        var sut = new NavigationHistoryService();

        Assert.False(sut.CanGoBack);
        Assert.False(sut.CanGoForward);
        Assert.Null(sut.GoBack(Loc("/a", 0, 0)));
        Assert.Null(sut.GoForward(Loc("/a", 0, 0)));
    }

    [Fact]
    public void Record_then_GoBack_returns_recorded_location()
    {
        var sut = new NavigationHistoryService();

        sut.Record(Loc("/a.cs", 5, 0));
        var got = sut.GoBack(Loc("/b.cs", 1, 1));

        Assert.Equal(Loc("/a.cs", 5, 0), got);
        Assert.False(sut.CanGoBack);
        Assert.True(sut.CanGoForward);
    }

    [Fact]
    public void GoForward_after_GoBack_returns_the_pre_back_location()
    {
        var sut = new NavigationHistoryService();
        sut.Record(Loc("/a.cs", 5, 0)); // user was here
        // current is "b.cs" — pretend they navigated to it
        sut.GoBack(Loc("/b.cs", 1, 1));   // now at /a.cs
        var got = sut.GoForward(Loc("/a.cs", 5, 0));

        Assert.Equal(Loc("/b.cs", 1, 1), got);
    }

    [Fact]
    public void Record_clears_forward_stack()
    {
        var sut = new NavigationHistoryService();
        sut.Record(Loc("/a.cs", 0, 0));
        sut.GoBack(Loc("/b.cs", 0, 0));
        Assert.True(sut.CanGoForward);

        // A new branch — recording a leave-position must invalidate the redo path.
        sut.Record(Loc("/c.cs", 0, 0));

        Assert.False(sut.CanGoForward);
    }

    [Fact]
    public void Record_coalesces_consecutive_duplicates()
    {
        var sut = new NavigationHistoryService();

        sut.Record(Loc("/a.cs", 5, 0));
        sut.Record(Loc("/a.cs", 5, 0));
        sut.Record(Loc("/a.cs", 5, 0));

        // Only one entry should be on the back stack — verify by walking it.
        Assert.NotNull(sut.GoBack(Loc("/x", 0, 0)));
        Assert.False(sut.CanGoBack);
    }

    [Fact]
    public void Clear_resets_both_stacks()
    {
        var sut = new NavigationHistoryService();
        sut.Record(Loc("/a.cs", 0, 0));
        sut.GoBack(Loc("/b.cs", 0, 0));

        sut.Clear();

        Assert.False(sut.CanGoBack);
        Assert.False(sut.CanGoForward);
    }

    private static NavigationLocation Loc(string path, int row, int col) => new(path, row, col);
}
