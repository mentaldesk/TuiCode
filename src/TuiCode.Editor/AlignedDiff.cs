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
