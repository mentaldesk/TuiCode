namespace TuiCode.Abstractions;

/// <summary>English nouns that agree with a count, so one reads <c>1 file</c> and more <c>2 files</c> (#536).</summary>
public static class Plural
{
    /// <summary><paramref name="singular"/> for a count of one, otherwise its plural: <c>file</c> → <c>files</c>, <c>match</c> → <c>matches</c>.</summary>
    public static string Of(long count, string singular) =>
        count == 1 ? singular
        : singular.EndsWith('s') || singular.EndsWith('x') || singular.EndsWith("ch", StringComparison.Ordinal) || singular.EndsWith("sh", StringComparison.Ordinal) ? singular + "es"
        : singular + "s";

    /// <summary>The count, with thousands separators, and its noun: <c>1 line</c>, <c>1,024 lines</c>.</summary>
    public static string Count(long count, string singular) => $"{count:N0} {Of(count, singular)}";
}
