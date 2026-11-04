using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class TerminalPlaceTests
{
    [Theory]
    [InlineData("/code/TuiCode/main", "git@github.com:mentaldesk/TuiCode.git", "TuiCode/main")]
    [InlineData("/code/TuiCode/main", "https://github.com/mentaldesk/TuiCode", "TuiCode/main")]
    [InlineData("/code/tui-style-guide", "https://github.com/mentaldesk/tui-style-guide.git", "tui-style-guide")]
    [InlineData("/code/tuicode", "https://github.com/mentaldesk/TuiCode.git", "tuicode")]
    [InlineData("/code/notes", null, "notes")]
    [InlineData("/code/notes/", "/srv/git/journal.git/", "journal/notes")]
    public void Names_the_folder_after_its_repo_when_they_differ(string folder, string? origin, string place) =>
        Assert.Equal(place, TerminalPlace.For(folder, origin));
}
