using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

public class FolderPathTests
{
    [Theory]
    [InlineData("~/code/zmk", "/Users/me/code/zmk")]
    [InlineData("~", "/Users/me")]
    [InlineData("~/", "/Users/me")]
    [InlineData("~/code/zmk/", "/Users/me/code/zmk")]
    [InlineData(" /tmp/x/ ", "/tmp/x")]
    [InlineData("/", "/")]
    [InlineData("~other/x", "~other/x")]
    public void Expand_turns_a_leading_tilde_into_home_and_drops_a_trailing_separator(string typed, string expected) =>
        Assert.Equal(expected, FolderPath.Expand(typed, "/Users/me"));
}
