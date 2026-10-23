namespace TuiCode.Editor;

/// <summary>A file tab as it was when it closed: where it sat, where its cursor was, and its undo history (#578).</summary>
internal sealed record ClosedTab(IFileInfo File, int Group, int Index, ViewPosition Position, CodeEditor.UndoHistory History);

/// <summary>The file tabs closed this session, newest first, for Undo close tab (#578).</summary>
internal sealed class ClosedTabs
{
    public const int Max = 20;

    private readonly List<ClosedTab> _newestLast = [];

    public int Count => _newestLast.Count;

    public void Push(ClosedTab tab)
    {
        _newestLast.Add(tab);
        if (_newestLast.Count > Max) _newestLast.RemoveAt(0);
    }

    public void Clear() => _newestLast.Clear();

    public bool Any(Func<IFileInfo, bool> reopenable) => _newestLast.Any(t => reopenable(t.File));

    /// <summary>Takes the newest tab <paramref name="reopenable"/> accepts, dropping the newer ones it doesn't.</summary>
    public ClosedTab? Take(Func<IFileInfo, bool> reopenable)
    {
        while (_newestLast.Count > 0)
        {
            var tab = _newestLast[^1];
            _newestLast.RemoveAt(_newestLast.Count - 1);
            if (reopenable(tab.File)) return tab;
        }
        return null;
    }
}
