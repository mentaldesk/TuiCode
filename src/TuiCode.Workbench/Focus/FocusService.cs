using TuiCode.Abstractions;

namespace TuiCode.Workbench.Focus;

/// <summary>
/// Where the next key goes. <see cref="Tabs"/> is a mode over the focused editor, not a Terminal.Gui focus state,
/// and <see cref="FindBar"/> is the find bar over the active file — the sidebar's find pane is <see cref="Find"/>.
/// </summary>
public enum FocusRegion { Editor, Diff, Explorer, Find, FindBar, Review, Tabs }

/// <summary>
/// The single source of truth for the focused region (#227). Every focus move the workbench makes goes
/// through <see cref="Focus"/>; <see cref="Reconcile"/> re-reads the region from the view Terminal.Gui
/// reports as focused, so a move TG makes on its own — or one that didn't land — can't leave the record
/// stale. Terminal.Gui-free: the host supplies the focused view and each region's move and ownership test.
/// </summary>
/// <param name="focusedView">The view Terminal.Gui currently has the keyboard on, or null.</param>
public sealed class FocusService(Func<object?> focusedView)
{
    private sealed record Target(FocusRegion Region, Func<bool> Move, Func<object?, bool> Owns);

    private readonly List<Target> _targets = [];

    /// <summary>The region keys go to now.</summary>
    public FocusRegion Region { get; private set; } = FocusRegion.Editor;

    /// <summary>The last region a move couldn't reach, cleared by the next move that lands.</summary>
    public FocusRegion? Unreachable { get; private set; }

    /// <summary>Whether the view Terminal.Gui has the keyboard on is still in <see cref="Region"/>.</summary>
    public bool Holds => _targets.FirstOrDefault(t => t.Region == Region) is { } target && target.Owns(focusedView());

    public event EventHandler<FocusRegion>? RegionChanged;

    /// <param name="move">Moves focus into the region; false when there was nothing to focus.</param>
    /// <param name="owns">Whether the focused view belongs to the region.</param>
    public void Register(FocusRegion region, Func<bool> move, Func<object?, bool> owns) =>
        _targets.Add(new Target(region, move, owns));

    /// <summary>Moves focus to <paramref name="region"/> and records it; false when the move didn't land.</summary>
    public bool Focus(FocusRegion region)
    {
        if (_targets.FirstOrDefault(t => t.Region == region) is not { } target) return false;
        // Terminal.Gui's SetFocus reports false when the view already has focus, which is a move that landed.
        if (!target.Move() && !target.Owns(focusedView()))
        {
            Unreachable = region;
            return false;
        }
        Unreachable = null;
        Record(region);
        return true;
    }

    /// <summary>Re-reads the region from the view Terminal.Gui reports as focused.</summary>
    public void Reconcile()
    {
        var focused = focusedView();
        // The current region keeps focus until another claims it: Tabs owns the whole editor pane, so
        // cycling tabs from the strip doesn't read as a move into the editor body or a diff.
        if (_targets.FirstOrDefault(t => t.Region == Region) is { } current && current.Owns(focused)) return;
        foreach (var target in _targets)
            if (target.Owns(focused)) { Record(target.Region); return; }
    }

    /// <summary>Where a key looks for its binding while <paramref name="region"/> has focus.</summary>
    public static CommandScope ScopeOf(FocusRegion region) => region switch
    {
        FocusRegion.Explorer => CommandScope.Explorer,
        FocusRegion.Find => CommandScope.Find,
        FocusRegion.Diff => CommandScope.Diff,
        // The strip is a mode over the editor, which still holds Terminal.Gui's focus underneath it, and the
        // find bar answers its own keys from its layered scope; the rest still act on the file beneath it.
        FocusRegion.Editor or FocusRegion.Tabs or FocusRegion.FindBar => CommandScope.Editor,
        _ => CommandScope.Global,
    };

    /// <summary>The one word the status bar shows for <paramref name="region"/>.</summary>
    public static string Label(FocusRegion region) => region switch
    {
        FocusRegion.Editor => "Editor",
        FocusRegion.Diff => "Diff",
        FocusRegion.Explorer => "Explorer",
        FocusRegion.Find or FocusRegion.FindBar => "Find",
        FocusRegion.Review => "Review",
        _ => "Tabs",
    };

    private void Record(FocusRegion region)
    {
        if (region == Region) return;
        Region = region;
        RegionChanged?.Invoke(this, region);
    }
}
