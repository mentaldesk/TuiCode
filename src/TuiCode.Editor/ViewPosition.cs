namespace TuiCode.Editor;

/// <summary>Where the cursor was in a file and which part of it showed, zero-based: <paramref name="TopLine"/> is a file line even while wrapped.</summary>
public sealed record ViewPosition(int Line, int Column, int TopLine, int LeftColumn);
