namespace TuiCode.Workbench.Navigation;

/// <summary>A recorded cursor position: a file plus a zero-based row/column inside it.</summary>
public readonly record struct CursorLocation(string FilePath, int Row, int Column);

/// <summary>
/// Browser-style back/forward history of cursor locations (#35). Pure and TG-free so the
/// navigation logic is unit-tested directly; <see cref="TuiCode.Workbench.WorkbenchHost"/> is
/// the thin shell that feeds it cursor moves and drives the editor on Back/Forward.
///
/// The model is a single list with a cursor (<c>_current</c>) into it. <see cref="Visit"/>
/// decides whether an arriving position is a real <i>jump</i> (different file, a row delta past
/// the threshold, or an explicit jump like Go-to-line) — only jumps create a navigable entry.
/// Small same-file moves are treated as <i>drift</i>: they keep the current entry tracking the
/// live cursor so a later jump's Back target is exactly where the user was, but they never fill
/// the stack with every arrow press. A jump truncates any forward (redo) path, mirroring a
/// browser navigating after going back.
/// </summary>
public sealed class CursorLocationHistory
{
    private readonly List<CursorLocation> _entries = new();
    private int _current = -1;
    private readonly int _capacity;
    private readonly int _lineThreshold;

    public CursorLocationHistory(int capacity = 100, int lineThreshold = 10)
    {
        _capacity = Math.Max(2, capacity);
        _lineThreshold = Math.Max(1, lineThreshold);
    }

    public bool CanGoBack => _current > 0;
    public bool CanGoForward => _current >= 0 && _current < _entries.Count - 1;

    /// <summary>The entry the cursor is currently considered to be at, or null before any visit.</summary>
    public CursorLocation? Current => _current >= 0 ? _entries[_current] : null;

    /// <summary>Number of navigable entries — exposed for tests/diagnostics.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Record the cursor arriving at <paramref name="loc"/>. Returns true if it created a new
    /// history entry (a jump), false if it was drift, a duplicate, or the initial seed.
    /// </summary>
    public bool Visit(CursorLocation loc, bool explicitJump = false)
    {
        if (_current < 0)
        {
            // First position seen this session: seed the list without making it a jump,
            // so the very first real jump produces [origin, destination].
            _entries.Add(loc);
            _current = 0;
            return false;
        }

        var current = _entries[_current];
        if (current == loc) return false;

        var sameFile = string.Equals(current.FilePath, loc.FilePath, StringComparison.Ordinal);
        var isJump = explicitJump || !sameFile || Math.Abs(loc.Row - current.Row) >= _lineThreshold;

        if (!isJump)
        {
            _entries[_current] = loc;
            return false;
        }

        if (_current < _entries.Count - 1)
            _entries.RemoveRange(_current + 1, _entries.Count - _current - 1);
        _entries.Add(loc);
        _current = _entries.Count - 1;
        TrimToCapacity();
        return true;
    }

    /// <summary>Step back one entry and return it, or null if there's nothing behind.</summary>
    public CursorLocation? GoBack()
    {
        if (!CanGoBack) return null;
        return _entries[--_current];
    }

    /// <summary>Step forward one entry along the redo path and return it, or null if at the front.</summary>
    public CursorLocation? GoForward()
    {
        if (!CanGoForward) return null;
        return _entries[++_current];
    }

    private void TrimToCapacity()
    {
        var overflow = _entries.Count - _capacity;
        if (overflow <= 0) return;
        _entries.RemoveRange(0, overflow);
        _current = Math.Max(0, _current - overflow);
    }
}
