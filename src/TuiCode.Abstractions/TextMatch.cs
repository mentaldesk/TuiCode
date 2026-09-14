namespace TuiCode.Abstractions;

/// <summary>
/// A search hit inside a document: zero-based <paramref name="Row"/>, and a zero-based
/// <paramref name="Column"/> / <paramref name="Length"/> measured in UTF-16 chars of that line
/// (line terminators excluded).
/// </summary>
public readonly record struct TextMatch(int Row, int Column, int Length)
{
    public int End => Column + Length;
}
