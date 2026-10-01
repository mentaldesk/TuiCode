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

/// <summary>Classifies each current line against a baseline (the file at <c>HEAD</c>, else the last save) for the gutter (#23).</summary>
public static class LineDiff
{
    // Past this many line edits the diff gives up and marks the whole differing region modified.
    internal const int MaxEdits = 500;

    public static LineChange[] Compute(IReadOnlyList<string> baseline, IReadOnlyList<string> current)
    {
        var changes = new LineChange[current.Count];
        var hunks = new List<Hunk>();
        AddHunks(hunks, baseline, 0, baseline.Count, current, 0, current.Count, MaxEdits, anchored: true);
        foreach (var hunk in hunks)
            MarkHunk(changes, hunk.NewStart, hunk.NewCount, hunk.OldCount);
        return changes;
    }

    /// <summary>The runs of lines that differ, in order; each replaces OldCount lines of a at OldStart with NewCount lines of b at NewStart.</summary>
    internal static List<Hunk> Hunks(IReadOnlyList<string> a, IReadOnlyList<string> b, int maxEdits = MaxEdits)
    {
        var hunks = new List<Hunk>();
        AddHunks(hunks, a, 0, a.Count, b, 0, b.Count, maxEdits, anchored: false);
        return hunks;
    }

    // Anchored, a region past maxEdits is split at lines unique to each side (as patience diff does), not marked whole.
    private static void AddHunks(
        List<Hunk> hunks, IReadOnlyList<string> a, int aStart, int aEnd, IReadOnlyList<string> b, int bStart, int bEnd,
        int maxEdits, bool anchored)
    {
        while (aStart < aEnd && bStart < bEnd && a[aStart] == b[bStart])
        {
            aStart++;
            bStart++;
        }
        while (aStart < aEnd && bStart < bEnd && a[aEnd - 1] == b[bEnd - 1])
        {
            aEnd--;
            bEnd--;
        }

        var oldLength = aEnd - aStart;
        var newLength = bEnd - bStart;
        if (oldLength == 0 && newLength == 0) return;

        if (EditScript(a, aStart, oldLength, b, bStart, newLength, maxEdits) is not { } edits)
        {
            if (anchored && Anchors(a, aStart, aEnd, b, bStart, bEnd) is { Count: > 0 } anchors)
            {
                foreach (var (oldAnchor, newAnchor) in anchors)
                {
                    AddHunks(hunks, a, aStart, oldAnchor, b, bStart, newAnchor, maxEdits, anchored);
                    aStart = oldAnchor + 1;
                    bStart = newAnchor + 1;
                }
                AddHunks(hunks, a, aStart, aEnd, b, bStart, bEnd, maxEdits, anchored);
            }
            else
            {
                hunks.Add(new Hunk(aStart, oldLength, bStart, newLength));
            }
            return;
        }

        int oldRow = aStart, newRow = bStart, inserted = 0, deleted = 0;
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
    }

    // The longest run, in order on both sides, of lines that occur exactly once in each range.
    private static List<(int Old, int New)> Anchors(
        IReadOnlyList<string> a, int aStart, int aEnd, IReadOnlyList<string> b, int bStart, int bEnd)
    {
        var counts = new Dictionary<string, (int Old, int OldAt, int New, int NewAt)>(StringComparer.Ordinal);
        for (var i = aStart; i < aEnd; i++)
            counts[a[i]] = counts.TryGetValue(a[i], out var c) ? c with { Old = c.Old + 1 } : (1, i, 0, -1);
        for (var j = bStart; j < bEnd; j++)
            if (counts.TryGetValue(b[j], out var c))
                counts[b[j]] = c with { New = c.New + 1, NewAt = j };

        var unique = counts.Values.Where(c => c is { Old: 1, New: 1 }).OrderBy(c => c.OldAt).ToArray();
        // Longest increasing subsequence of the new positions.
        var tails = new List<int>();
        var previous = new int[unique.Length];
        for (var i = 0; i < unique.Length; i++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (unique[tails[mid]].NewAt < unique[i].NewAt) lo = mid + 1;
                else hi = mid;
            }
            previous[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(i);
            else tails[lo] = i;
        }

        var anchors = new List<(int Old, int New)>();
        for (var i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = previous[i])
            anchors.Add((unique[i].OldAt, unique[i].NewAt));
        anchors.Reverse();
        return anchors;
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

    // Myers' O(ND) diff of a[aStart..aStart+n) against b[bStart..bStart+m); null if it needs more than maxEdits.
    private static List<Edit>? EditScript(IReadOnlyList<string> a, int aStart, int n, IReadOnlyList<string> b, int bStart, int m, int maxEdits)
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
                while (x < n && y < m && a[aStart + x] == b[bStart + y])
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
