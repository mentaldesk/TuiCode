using System.Runtime.CompilerServices;

namespace TuiCode.Editor;

/// <summary>A TextView model's lines as strings, re-reading only the lines that changed since the last refresh.</summary>
internal sealed class LineSnapshot
{
    private readonly List<List<Cell>> _sources = [];
    private readonly List<int> _versions = [];
    private readonly List<string> _lines = [];

    /// <summary>Brings the snapshot up to date with <paramref name="model"/>. The returned list is updated in place by later refreshes.</summary>
    public IReadOnlyList<string> Refresh(List<List<Cell>> model)
    {
        var prefix = 0;
        while (prefix < model.Count && prefix < _lines.Count && IsUnchanged(prefix, model[prefix]))
            prefix++;
        var suffix = 0;
        while (suffix < model.Count - prefix && suffix < _lines.Count - prefix
               && IsUnchanged(_lines.Count - 1 - suffix, model[model.Count - 1 - suffix]))
            suffix++;

        var stale = _lines.Count - prefix - suffix;
        var fresh = model.Count - prefix - suffix;
        if (stale > fresh)
        {
            _sources.RemoveRange(prefix + fresh, stale - fresh);
            _versions.RemoveRange(prefix + fresh, stale - fresh);
            _lines.RemoveRange(prefix + fresh, stale - fresh);
        }
        else if (fresh > stale)
        {
            _sources.InsertRange(prefix + stale, new List<Cell>[fresh - stale]);
            _versions.InsertRange(prefix + stale, new int[fresh - stale]);
            _lines.InsertRange(prefix + stale, new string[fresh - stale]);
        }

        for (var i = prefix; i < prefix + fresh; i++)
        {
            _sources[i] = model[i];
            _versions[i] = Version(model[i]);
            _lines[i] = Cell.ToString(model[i]);
        }
        return _lines;
    }

    private bool IsUnchanged(int index, List<Cell> line) =>
        ReferenceEquals(_sources[index], line) && _versions[index] == Version(line);

    // TG edits lines in place; List<T> bumps this private counter on every mutation.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_version")]
    private static extern ref int Version(List<Cell> line);
}
