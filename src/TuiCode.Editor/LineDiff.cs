namespace TuiCode.Editor;

public enum LineChange
{
    None,
    Added,
    Modified,
    /// <summary>One or more baseline lines were removed immediately above this line.</summary>
    Deleted,
}

internal readonly record struct Hunk(int OldStart, int OldCount, int NewStart, int NewCount);

/// <summary>Classifies each current line against a baseline (the last-saved buffer) for the gutter (#23).</summary>
public static class LineDiff
{
    // Past this many line edits the diff gives up and marks the whole differing region modified.
    internal const int MaxEdits = 500;

    public static LineChange[] Compute(IReadOnlyList<string> baseline, IReadOnlyList<string> current)
    {
        var changes = new LineChange[current.Count];
        foreach (var hunk in Hunks(baseline, current))
            MarkHunk(changes, hunk.NewStart, hunk.NewCount, hunk.OldCount);
        return changes;
    }

    /// <summary>The runs of lines that differ, in order; each replaces OldCount lines of a at OldStart with NewCount lines of b at NewStart.</summary>
    internal static List<Hunk> Hunks(IReadOnlyList<string> a, IReadOnlyList<string> b, int maxEdits = MaxEdits)
    {
        var hunks = new List<Hunk>();

        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix
               && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix])
            suffix++;

        var oldLength = a.Count - prefix - suffix;
        var newLength = b.Count - prefix - suffix;
        if (oldLength == 0 && newLength == 0) return hunks;

        if (EditScript(a, b, prefix, oldLength, newLength, maxEdits) is not { } edits)
        {
            hunks.Add(new Hunk(prefix, oldLength, prefix, newLength));
            return hunks;
        }

        int oldRow = prefix, newRow = prefix, inserted = 0, deleted = 0;
        foreach (var edit in edits)
        {
            switch (edit)
            {
                case Edit.Equal:
                    if (inserted > 0 || deleted > 0)
                        hunks.Add(new Hunk(oldRow - deleted, deleted, newRow - inserted, inserted));
                    inserted = deleted = 0;
                    oldRow++;
                    newRow++;
                    break;
                case Edit.Insert:
                    inserted++;
                    newRow++;
                    break;
                case Edit.Delete:
                    deleted++;
                    oldRow++;
                    break;
            }
        }
        if (inserted > 0 || deleted > 0)
            hunks.Add(new Hunk(oldRow - deleted, deleted, newRow - inserted, inserted));
        return hunks;
    }

    private static void MarkHunk(LineChange[] changes, int start, int inserted, int deleted)
    {
        if (inserted > 0)
        {
            var kind = deleted > 0 ? LineChange.Modified : LineChange.Added;
            Array.Fill(changes, kind, start, inserted);
        }
        else if (deleted > 0 && changes.Length > 0)
        {
            // A deletion at the very end has no line below it, so it lands on the last line.
            var row = Math.Min(start, changes.Length - 1);
            if (changes[row] == LineChange.None) changes[row] = LineChange.Deleted;
        }
    }

    private enum Edit { Equal, Insert, Delete }

    // Myers' O(ND) diff of a[start..start+n) against b[start..start+m); null if it needs more than maxEdits.
    private static List<Edit>? EditScript(IReadOnlyList<string> a, IReadOnlyList<string> b, int start, int n, int m, int maxEdits)
    {
        var max = Math.Min(n + m, maxEdits);
        var offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();

        for (var d = 0; d <= max; d++)
        {
            trace.Add(v[(offset - d - 1)..(offset + d + 2)]);
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[start + x] == b[start + y])
                {
                    x++;
                    y++;
                }
                v[offset + k] = x;
                if (x >= n && y >= m) return Backtrack(trace, n, m);
            }
        }
        return null;
    }

    private static List<Edit> Backtrack(List<int[]> trace, int n, int m)
    {
        var edits = new List<Edit>();
        int x = n, y = m;
        for (var d = trace.Count - 1; d >= 0; d--)
        {
            // trace[d] holds v before round d, for diagonals -d-1..d+1.
            var v = trace[d];
            var k = x - y;
            var prevK = k == -d || (k != d && v[k - 1 + d + 1] < v[k + 1 + d + 1]) ? k + 1 : k - 1;
            var prevX = v[prevK + d + 1];
            var prevY = prevX - prevK;
            while (x > prevX && y > prevY)
            {
                edits.Add(Edit.Equal);
                x--;
                y--;
            }
            if (d > 0) edits.Add(x == prevX ? Edit.Insert : Edit.Delete);
            x = prevX;
            y = prevY;
        }
        edits.Reverse();
        return edits;
    }
}
