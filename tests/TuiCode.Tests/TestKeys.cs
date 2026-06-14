namespace TuiCode.Tests;

/// <summary>
/// Test sugar: build a canonical chord (the <see cref="Key"/> list that is now a binding's identity,
/// issue #89) from a readable sequence string like "Ctrl+S" or "Ctrl+W X". Production input comes
/// from captured keystrokes, not parsed strings — this helper exists only so tests stay legible.
/// </summary>
internal static class TestKeys
{
    public static Key[] Chord(string sequence) =>
        sequence
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => Key.TryParse(part, out var k)
                ? k
                : throw new InvalidOperationException($"Bad key '{part}' in '{sequence}'"))
            .ToArray();
}
