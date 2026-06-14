using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class CursorLocationHistoryTests
{
    private static CursorLocation Loc(string file, int row, int col = 0) => new(file, row, col);

    [Fact]
    public void Fresh_history_cannot_navigate_either_way()
    {
        var history = new CursorLocationHistory();
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
        Assert.Null(history.GoBack());
        Assert.Null(history.GoForward());
    }

    [Fact]
    public void First_visit_seeds_without_creating_a_navigable_jump()
    {
        var history = new CursorLocationHistory();
        Assert.False(history.Visit(Loc("a", 0)));
        Assert.False(history.CanGoBack);
        Assert.Equal(Loc("a", 0), history.Current);
    }

    [Fact]
    public void A_far_same_file_move_is_a_jump_that_back_returns_from()
    {
        var history = new CursorLocationHistory(lineThreshold: 10);
        history.Visit(Loc("a", 0));
        Assert.True(history.Visit(Loc("a", 100)));

        Assert.True(history.CanGoBack);
        Assert.Equal(Loc("a", 0), history.GoBack());
        Assert.Equal(Loc("a", 100), history.GoForward());
    }

    [Fact]
    public void A_small_same_file_move_is_drift_and_does_not_stack()
    {
        var history = new CursorLocationHistory(lineThreshold: 10);
        history.Visit(Loc("a", 0));
        Assert.False(history.Visit(Loc("a", 3)));

        Assert.False(history.CanGoBack);
        Assert.Equal(1, history.Count);
        // Drift keeps the current entry tracking the live cursor.
        Assert.Equal(Loc("a", 3), history.Current);
    }

    [Fact]
    public void Drift_updates_the_origin_a_later_jump_returns_to()
    {
        var history = new CursorLocationHistory(lineThreshold: 10);
        history.Visit(Loc("a", 0));
        history.Visit(Loc("a", 5));    // drift — origin becomes row 5
        history.Visit(Loc("a", 500));  // jump

        Assert.Equal(Loc("a", 5), history.GoBack());
    }

    [Fact]
    public void Switching_files_is_always_a_jump_even_within_the_threshold()
    {
        var history = new CursorLocationHistory(lineThreshold: 10);
        history.Visit(Loc("a", 1));
        Assert.True(history.Visit(Loc("b", 1)));

        Assert.Equal(Loc("a", 1), history.GoBack());
        Assert.Equal(Loc("b", 1), history.GoForward());
    }

    [Fact]
    public void An_explicit_jump_records_even_a_short_hop()
    {
        var history = new CursorLocationHistory(lineThreshold: 10);
        history.Visit(Loc("a", 0));
        Assert.True(history.Visit(Loc("a", 2), explicitJump: true));

        Assert.Equal(Loc("a", 0), history.GoBack());
    }

    [Fact]
    public void Exact_duplicate_positions_are_ignored()
    {
        var history = new CursorLocationHistory();
        history.Visit(Loc("a", 0));
        Assert.False(history.Visit(Loc("a", 0)));
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void A_jump_after_going_back_truncates_the_forward_path()
    {
        var history = new CursorLocationHistory(lineThreshold: 10);
        history.Visit(Loc("a", 0));
        history.Visit(Loc("a", 100));
        history.Visit(Loc("a", 200));

        history.GoBack(); // back to row 100
        Assert.True(history.CanGoForward);

        history.Visit(Loc("a", 999)); // new jump kills the redo path
        Assert.False(history.CanGoForward);
        Assert.Equal(Loc("a", 100), history.GoBack());
        Assert.Equal(Loc("a", 999), history.GoForward());
    }

    [Fact]
    public void Forward_walks_the_redo_path_back_up()
    {
        var history = new CursorLocationHistory(lineThreshold: 10);
        history.Visit(Loc("a", 0));
        history.Visit(Loc("a", 100));
        history.Visit(Loc("a", 200));

        history.GoBack();
        history.GoBack();
        Assert.Equal(Loc("a", 0), history.Current);
        Assert.Equal(Loc("a", 100), history.GoForward());
        Assert.Equal(Loc("a", 200), history.GoForward());
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void Capacity_trims_the_oldest_entries()
    {
        var history = new CursorLocationHistory(capacity: 3, lineThreshold: 1);
        for (var row = 0; row < 6; row++)
            history.Visit(Loc("a", row * 10));

        Assert.Equal(3, history.Count);
        // Newest is current; we can still walk back across the retained window.
        Assert.Equal(Loc("a", 40), history.GoBack());
        Assert.Equal(Loc("a", 30), history.GoBack());
        Assert.False(history.CanGoBack);
    }
}
