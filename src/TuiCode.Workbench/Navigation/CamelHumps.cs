namespace TuiCode.Workbench.Navigation;

/// <summary>
/// JetBrains-style CamelHumps matching (#109), case-insensitive: each query character continues
/// the previous match or starts a new hump, so <c>fdl</c> and <c>filedir</c> match <c>FileDirectoryListing</c>.
/// A space forces the next character onto a hump.
/// </summary>
internal static class CamelHumps
{
    /// <summary>Lower is better (0 is a prefix match); null when <paramref name="query"/> doesn't match.</summary>
    public static int? Score(string query, string candidate)
    {
        var chars = new List<char>(query.Length);
        var afterSpace = new List<bool>(query.Length);
        var pendingSpace = false;
        foreach (var ch in query)
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = true; continue; }
            chars.Add(char.ToLowerInvariant(ch));
            afterSpace.Add(pendingSpace);
            pendingSpace = false;
        }
        return Best(chars, afterSpace, candidate, 0, -1, new Dictionary<(int, int), int?>());
    }

    /// <summary>The last <c>/</c>-separated query part matches the last segment; earlier parts match earlier segments, in order.</summary>
    public static int? ScorePath(string query, IReadOnlyList<string> segments)
    {
        var parts = query.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return 0;
        if (segments.Count == 0 || Score(parts[^1], segments[^1]) is not { } total) return null;

        var s = segments.Count - 2;
        for (var p = parts.Length - 2; p >= 0; p--, s--)
        {
            int? score = null;
            while (s >= 0 && (score = Score(parts[p], segments[s])) is null) s--;
            if (score is null) return null;
            total += score.Value;
        }
        return total;
    }

    private static int? Best(List<char> q, List<bool> afterSpace, string c, int qi, int prev, Dictionary<(int, int), int?> memo)
    {
        if (qi == q.Count) return 0;
        if (memo.TryGetValue((qi, prev), out var cached)) return cached;

        int? best = null;
        for (var j = prev + 1; j < c.Length && best != 0; j++)
        {
            if (char.ToLowerInvariant(c[j]) != q[qi]) continue;

            var contiguous = j == prev + 1 && !afterSpace[qi];
            if (!contiguous && !IsHumpStart(c, j)) continue;
            if (Best(q, afterSpace, c, qi + 1, j, memo) is not { } rest) continue;

            var cost = rest + (contiguous ? 0 : 1);
            if (best is null || cost < best) best = cost;
        }
        memo[(qi, prev)] = best;
        return best;
    }

    private static bool IsHumpStart(string c, int j)
    {
        if (j == 0) return true;
        var ch = c[j];
        var before = c[j - 1];
        if (!char.IsLetterOrDigit(ch) || !char.IsLetterOrDigit(before)) return true;
        if (char.IsUpper(ch) && (!char.IsUpper(before) || j + 1 < c.Length && char.IsLower(c[j + 1]))) return true;
        return char.IsDigit(ch) != char.IsDigit(before);
    }
}
