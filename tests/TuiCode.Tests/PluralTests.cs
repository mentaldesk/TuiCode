using System.Globalization;
using TuiCode.Abstractions;

namespace TuiCode.Tests;

public class PluralTests
{
    [Theory]
    [InlineData(0, "file", "files")]
    [InlineData(1, "file", "file")]
    [InlineData(2, "file", "files")]
    [InlineData(1_000_000, "file", "files")]
    [InlineData(1, "match", "match")]
    [InlineData(2, "match", "matches")]
    [InlineData(2, "box", "boxes")]
    [InlineData(2, "crash", "crashes")]
    [InlineData(2, "class", "classes")]
    public void Of_is_singular_only_for_one(long count, string singular, string expected) =>
        Assert.Equal(expected, Plural.Of(count, singular));

    [Theory]
    [InlineData(0, "0 lines")]
    [InlineData(1, "1 line")]
    [InlineData(2, "2 lines")]
    [InlineData(1234, "1,234 lines")]
    public void Count_prefixes_the_count_with_thousands_separators(long count, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { Assert.Equal(expected, Plural.Count(count, "line")); }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
