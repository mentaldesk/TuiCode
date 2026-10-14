using TuiCode.Syntax;

namespace TuiCode.Editor;

/// <summary>A place in the buffer: a row and a UTF-16 char index into its line.</summary>
internal readonly record struct TextSpot(int Row, int Char);

/// <param name="Partner">Null when <see cref="Bracket"/> has none, or when <paramref name="GaveUp"/> the search before finding it.</param>
internal sealed record BracketPair(TextSpot Bracket, TextSpot? Partner, bool GaveUp = false);

/// <summary>
/// Pairs <c>()</c>, <c>[]</c> and <c>{}</c> (#539), skipping those in strings and comments. <paramref name="tokens"/>
/// gives a row's TextMate tokens, empty for plain text, or null when the row isn't lexed yet, which ends the search
/// unanswered, as does going past <see cref="SearchLimit"/> chars.
/// </summary>
internal sealed class Brackets(IReadOnlyList<string> lines, Func<int, int[]?> tokens)
{
    public const int SearchLimit = 200_000;

    private const string Openers = "([{";
    private const string Closers = ")]}";

    /// <summary>The bracket at <paramref name="caret"/>, else the one just before it, and its partner; null when there's neither.</summary>
    public BracketPair? At(TextSpot caret)
    {
        if ((BracketAt(caret) ?? BracketAt(caret with { Char = caret.Char - 1 })) is not { } bracket) return null;
        var (found, partner) = Partner(bracket);
        return found switch
        {
            Search.Found => new BracketPair(bracket, partner),
            Search.Missing => new BracketPair(bracket, null),
            _ => new BracketPair(bracket, null, GaveUp: true),
        };
    }

    /// <summary>The bracket that closes the innermost block around <paramref name="caret"/>, or null.</summary>
    public TextSpot? Closing(TextSpot caret)
    {
        Span<int> depth = stackalloc int[3];
        var scanned = 0;
        for (var row = caret.Row; row < lines.Count; row++)
        {
            var line = lines[row];
            var rowTokens = default(int[]);
            for (var i = row == caret.Row ? caret.Char : 0; i < line.Length; i++)
            {
                if (++scanned > SearchLimit) return null;
                var kind = Kind(line[i], out var opens);
                if (kind < 0) continue;
                if ((rowTokens ??= tokens(row)) is not { } known) return null;
                if (!IsCode(known, i)) continue;
                if (opens) depth[kind]++;
                else if (depth[kind]-- == 0) return new TextSpot(row, i);
            }
        }
        return null;
    }

    private enum Search { Found, Missing, GaveUp }

    private (Search, TextSpot) Partner(TextSpot bracket)
    {
        var c = lines[bracket.Row][bracket.Char];
        var forward = Kind(c, out _) is var kind && Openers[kind] == c;
        var (self, partner) = forward ? (Openers[kind], Closers[kind]) : (Closers[kind], Openers[kind]);
        var depth = 0;
        var scanned = 0;
        var step = forward ? 1 : -1;
        for (var row = bracket.Row; row >= 0 && row < lines.Count; row += step)
        {
            var line = lines[row];
            var rowTokens = default(int[]);
            var i = row == bracket.Row ? bracket.Char + step : forward ? 0 : line.Length - 1;
            for (; i >= 0 && i < line.Length; i += step)
            {
                if (++scanned > SearchLimit) return (Search.GaveUp, default);
                var next = line[i];
                if (next != self && next != partner) continue;
                if ((rowTokens ??= tokens(row)) is not { } known) return (Search.GaveUp, default);
                if (!IsCode(known, i)) continue;
                if (next == self) depth++;
                else if (depth-- == 0) return (Search.Found, new TextSpot(row, i));
            }
        }
        return (Search.Missing, default);
    }

    private TextSpot? BracketAt(TextSpot spot)
    {
        if (spot.Row < 0 || spot.Row >= lines.Count) return null;
        var line = lines[spot.Row];
        if (spot.Char < 0 || spot.Char >= line.Length || Kind(line[spot.Char], out _) < 0) return null;
        return tokens(spot.Row) is { } rowTokens && IsCode(rowTokens, spot.Char) ? spot : null;
    }

    private static int Kind(char c, out bool opens)
    {
        var open = Openers.IndexOf(c);
        opens = open >= 0;
        return opens ? open : Closers.IndexOf(c);
    }

    // Tokens are (UTF-16 start, metadata) pairs in order.
    private static bool IsCode(int[] tokens, int index)
    {
        if (tokens.Length == 0) return true;
        var (low, high) = (0, tokens.Length / 2 - 1);
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (tokens[mid * 2] <= index) low = mid;
            else high = mid - 1;
        }
        return SyntaxHighlighter.IsCode(tokens[low * 2 + 1]);
    }
}
