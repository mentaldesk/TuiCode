namespace TuiCode.Editor;

public enum DiffSide
{
    Left,
    Right,
}

/// <summary>
/// A find match in a diff (#413): <paramref name="Row"/> is a row of <see cref="DiffTab.Diff"/>, and
/// <paramref name="Column"/> / <paramref name="Length"/> are UTF-16 chars of that side's line.
/// </summary>
public readonly record struct DiffMatch(int Row, DiffSide Side, int Column, int Length);
