namespace TuiCode.Editor;

/// <summary>Where a diff sits in the Review tab's file list, so it can be stepped through and placed (#181).</summary>
/// <param name="Key">Identifies the review, so a diff left from an earlier one isn't stepped from.</param>
/// <param name="Path">The file's repo-relative path in the review.</param>
public sealed record ReviewSpot(string Key, int Index, int Count, string Path)
{
    public string Label => $"File {Index + 1} of {Count}";
}
