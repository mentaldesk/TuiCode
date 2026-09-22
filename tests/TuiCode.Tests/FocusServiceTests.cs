using TuiCode.Abstractions;
using TuiCode.Workbench.Focus;

namespace TuiCode.Tests;

// FocusService is Terminal.Gui-free: regions are named by a stand-in "view" object here (#227).
public class FocusServiceTests
{
    private object? _focused;
    private readonly FocusService _focus;

    public FocusServiceTests()
    {
        _focus = new FocusService(() => _focused);
        Register(FocusRegion.Find, "find");
        Register(FocusRegion.Explorer, "explorer");
        Register(FocusRegion.Editor, "editor");
    }

    [Fact]
    public void Focus_moves_to_the_region_and_records_it()
    {
        var moved = _focus.Focus(FocusRegion.Explorer);

        Assert.True(moved);
        Assert.Equal(FocusRegion.Explorer, _focus.Region);
        Assert.Equal("explorer", _focused);
        Assert.Null(_focus.Unreachable);
    }

    [Fact]
    public void Focus_announces_only_a_region_it_wasnt_already_in()
    {
        var announced = new List<FocusRegion>();
        _focus.RegionChanged += (_, region) => announced.Add(region);

        _focus.Focus(FocusRegion.Explorer);
        _focus.Focus(FocusRegion.Explorer);
        _focus.Focus(FocusRegion.Find);

        Assert.Equal([FocusRegion.Explorer, FocusRegion.Find], announced);
    }

    [Fact]
    public void Focus_reports_a_move_that_didnt_land_and_leaves_the_region_alone()
    {
        _focus.Register(FocusRegion.Review, () => false, focused => Equals(focused, "review"));
        _focus.Focus(FocusRegion.Explorer);

        var moved = _focus.Focus(FocusRegion.Review);

        Assert.False(moved);
        Assert.Equal(FocusRegion.Review, _focus.Unreachable);
        Assert.Equal(FocusRegion.Explorer, _focus.Region);
    }

    // Terminal.Gui's SetFocus reports false when the view already has focus; that isn't a failed move.
    [Fact]
    public void Focus_counts_a_region_that_already_has_focus_as_landed()
    {
        _focus.Register(FocusRegion.Review, () => false, focused => Equals(focused, "review"));
        _focused = "review";

        var moved = _focus.Focus(FocusRegion.Review);

        Assert.True(moved);
        Assert.Equal(FocusRegion.Review, _focus.Region);
        Assert.Null(_focus.Unreachable);
    }

    [Fact]
    public void Reconcile_picks_up_a_focus_move_that_didnt_go_through_the_service()
    {
        _focus.Focus(FocusRegion.Editor);
        _focused = "find";

        _focus.Reconcile();

        Assert.Equal(FocusRegion.Find, _focus.Region);
    }

    [Fact]
    public void Reconcile_keeps_the_region_while_nothing_else_claims_the_focused_view()
    {
        _focus.Focus(FocusRegion.Find);
        _focused = "a modal";

        _focus.Reconcile();

        Assert.Equal(FocusRegion.Find, _focus.Region);
    }

    // Tabs is a mode over the editor, which keeps Terminal.Gui's focus underneath it.
    [Fact]
    public void Reconcile_leaves_a_region_that_still_owns_the_focused_view()
    {
        _focus.Register(FocusRegion.Tabs, () => true, focused => Equals(focused, "editor"));
        _focus.Focus(FocusRegion.Editor);
        _focus.Focus(FocusRegion.Tabs);

        _focus.Reconcile();

        Assert.Equal(FocusRegion.Tabs, _focus.Region);
    }

    [Theory]
    [InlineData(FocusRegion.Editor, CommandScope.Editor)]
    [InlineData(FocusRegion.Tabs, CommandScope.Editor)]
    [InlineData(FocusRegion.Diff, CommandScope.Diff)]
    [InlineData(FocusRegion.Explorer, CommandScope.Explorer)]
    [InlineData(FocusRegion.Find, CommandScope.Find)]
    [InlineData(FocusRegion.Review, CommandScope.Global)]
    public void ScopeOf_maps_each_region_to_the_scope_its_keys_fire_in(FocusRegion region, CommandScope scope) =>
        Assert.Equal(scope, FocusService.ScopeOf(region));

    [Theory]
    [InlineData(FocusRegion.Editor, "Editor")]
    [InlineData(FocusRegion.Diff, "Diff")]
    [InlineData(FocusRegion.Explorer, "Explorer")]
    [InlineData(FocusRegion.Find, "Find")]
    [InlineData(FocusRegion.Review, "Review")]
    [InlineData(FocusRegion.Tabs, "Tabs")]
    public void Label_names_every_region(FocusRegion region, string word) =>
        Assert.Equal(word, FocusService.Label(region));

    private void Register(FocusRegion region, string view) =>
        _focus.Register(region, () => { _focused = view; return true; }, focused => Equals(focused, view));
}
