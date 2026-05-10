namespace TuiCode.Abstractions;

/// <summary>
/// Browser-style back/forward stack of cursor locations the user has visited.
/// The service is dumb data: callers <see cref="Record"/> a "leaving" location
/// before each significant jump, and call <see cref="GoBack"/> /
/// <see cref="GoForward"/> to walk the history. Performing the actual cursor
/// move is the caller's job.
/// </summary>
public interface INavigationHistoryService
{
    bool CanGoBack { get; }
    bool CanGoForward { get; }

    /// <summary>
    /// Record the location the user is leaving. Pushes onto the back stack and
    /// clears the forward stack (a new branch invalidates the redo path).
    /// </summary>
    void Record(NavigationLocation location);

    /// <summary>
    /// Pop the top of the back stack and return it; pushes <paramref name="currentLocation"/>
    /// onto the forward stack so it can be revisited via <see cref="GoForward"/>.
    /// Returns null if the back stack is empty.
    /// </summary>
    NavigationLocation? GoBack(NavigationLocation currentLocation);

    /// <summary>
    /// Pop the top of the forward stack and return it; pushes <paramref name="currentLocation"/>
    /// onto the back stack. Returns null if the forward stack is empty.
    /// </summary>
    NavigationLocation? GoForward(NavigationLocation currentLocation);

    void Clear();
}

public sealed record NavigationLocation(string FilePath, int Row, int Column);
