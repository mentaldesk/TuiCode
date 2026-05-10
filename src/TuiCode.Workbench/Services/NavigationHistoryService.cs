using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

public sealed class NavigationHistoryService : INavigationHistoryService
{
    // Capacity is generous but bounded — keeps memory predictable on long sessions.
    private const int MaxEntries = 100;

    private readonly LinkedList<NavigationLocation> _back = new();
    private readonly LinkedList<NavigationLocation> _forward = new();

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void Record(NavigationLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);

        // Coalesce adjacent duplicates so repeated jumps to the same place don't
        // pollute the stack.
        if (_back.Count > 0 && _back.Last!.Value == location) return;

        _back.AddLast(location);
        TrimBack();
        _forward.Clear();
    }

    public NavigationLocation? GoBack(NavigationLocation currentLocation)
    {
        ArgumentNullException.ThrowIfNull(currentLocation);
        if (_back.Count == 0) return null;

        var target = _back.Last!.Value;
        _back.RemoveLast();
        _forward.AddLast(currentLocation);
        return target;
    }

    public NavigationLocation? GoForward(NavigationLocation currentLocation)
    {
        ArgumentNullException.ThrowIfNull(currentLocation);
        if (_forward.Count == 0) return null;

        var target = _forward.Last!.Value;
        _forward.RemoveLast();
        _back.AddLast(currentLocation);
        TrimBack();
        return target;
    }

    public void Clear()
    {
        _back.Clear();
        _forward.Clear();
    }

    private void TrimBack()
    {
        while (_back.Count > MaxEntries)
            _back.RemoveFirst();
    }
}
