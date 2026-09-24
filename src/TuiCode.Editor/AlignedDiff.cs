namespace TuiCode.Editor;

public enum DiffRowKind
{
    Both,
    Modified,
    LeftOnly,
    RightOnly,
}

/// <summary>One side-by-side row; <see cref="Left"/> and <see cref="Right"/> are line indexes, null where that side has a gap.</summary>
public readonly record struct DiffRow(DiffRowKind Kind, int? Left, int? Right);

/// <summary>A change block's rows, and the lines each side has in it; the right side's start is where to write when it has none.</summary>
public readonly record struct ChangeBlock(int FirstRow, int LastRow, int LeftStart, int LeftCount, int RightStart, int RightCount);

/// <summary>A line diff of another version (left) against the buffer (right), laid out as aligned side-by-side rows (#162).</summary>
public sealed class AlignedDiff
{
    private readonly int _rightCount;

    private AlignedDiff(List<DiffRow> rows, List<int> changeBlocks, int rightCount)
    {
        Rows = rows;
        ChangeBlocks = changeBlocks;
        _rightCount = rightCount;
    }

    public IReadOnlyList<DiffRow> Rows { get; }

    /// <summary>The index of each change block's first row, in order.</summary>
    public IReadOnlyList<int> ChangeBlocks { get; }

    public static AlignedDiff Compute(IReadOnlyList<string> left, IReadOnlyList<string> right, int maxEdits = LineDiff.MaxEdits)
    {
        var rows = new List<DiffRow>();
        var changeBlocks = new List<int>();
        int l = 0, r = 0;
        foreach (var hunk in LineDiff.Hunks(left, right, maxEdits))
        {
            while (l < hunk.OldStart)
                rows.Add(new DiffRow(DiffRowKind.Both, l++, r++));

            changeBlocks.Add(rows.Count);
            var paired = Math.Min(hunk.OldCount, hunk.NewCount);
            for (var i = 0; i < paired; i++)
                rows.Add(new DiffRow(DiffRowKind.Modified, l++, r++));
            while (l < hunk.OldStart + hunk.OldCount)
                rows.Add(new DiffRow(DiffRowKind.LeftOnly, l++, null));
            while (r < hunk.NewStart + hunk.NewCount)
                rows.Add(new DiffRow(DiffRowKind.RightOnly, null, r++));
        }
        while (l < left.Count)
            rows.Add(new DiffRow(DiffRowKind.Both, l++, r++));

        return new AlignedDiff(rows, changeBlocks, right.Count);
    }

    /// <summary>The block of changed rows starting at <paramref name="start"/>, one of <see cref="ChangeBlocks"/>.</summary>
    public ChangeBlock Block(int start)
    {
        var last = start;
        while (last + 1 < Rows.Count && Rows[last + 1].Kind != DiffRowKind.Both)
            last++;

        int leftStart = -1, leftCount = 0, rightStart = -1, rightCount = 0;
        for (var row = start; row <= last; row++)
        {
            if (Rows[row].Left is { } left)
            {
                if (leftStart < 0) leftStart = left;
                leftCount++;
            }
            if (Rows[row].Right is not { } right) continue;
            if (rightStart < 0) rightStart = right;
            rightCount++;
        }
        return new ChangeBlock(start, last, Math.Max(leftStart, 0), leftCount, rightStart < 0 ? RightAfter(last) : rightStart, rightCount);
    }

    // Where a block with nothing on the right sits in the buffer: the next right line below it, or the end.
    private int RightAfter(int lastRow)
    {
        for (var row = lastRow + 1; row < Rows.Count; row++)
        {
            if (Rows[row].Right is { } right) return right;
        }
        return _rightCount;
    }

    /// <summary>The buffer line a row maps to: its right line, or for a gap on the right the next right line below it.</summary>
    public int BufferLine(int row)
    {
        for (var i = row; i < Rows.Count; i++)
        {
            if (Rows[i].Right is { } right)
                return right;
        }
        return Math.Max(0, _rightCount - 1);
    }
}
