using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

public class RecentFolderListTests
{
    private const string Home = "/Users/me";

    [Fact]
    public void Rows_keeps_the_history_order_and_leaves_out_the_current_folder_and_folders_that_are_gone()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/Users/me/code/a");
        fs.AddDirectory("/Users/me/code/b");
        fs.AddDirectory("/Users/me/code/c");
        string[] history = ["/Users/me/code/a", "/Users/me/code/gone", "/Users/me/code/c", "/Users/me/code/b"];

        var rows = RecentFolderList.Rows(history, "/Users/me/code/a", fs.Directory.Exists, Home);

        Assert.Equal(["/Users/me/code/c", "/Users/me/code/b"], rows.Select(r => r.Path));
    }

    [Fact]
    public void Rows_treats_a_trailing_separator_as_the_same_current_folder()
    {
        var rows = RecentFolderList.Rows(["/Users/me/code/a/", "/Users/me/code/b"], "/Users/me/code/a", _ => true, Home);

        Assert.Equal(["/Users/me/code/b"], rows.Select(r => r.Path));
    }

    [Theory]
    [InlineData("/Users/me/code/TuiCode/save-conflict", "save-conflict", "~/code/TuiCode")]
    [InlineData("/Users/me/code", "code", "~")]
    [InlineData("/Users/me", "me", "/Users")]
    [InlineData("/Users/meadow/x", "x", "/Users/meadow")]
    [InlineData("/tmp", "tmp", "/")]
    [InlineData("/", "/", "")]
    [InlineData(@"C:\work\repo", "repo", @"C:\work")]
    [InlineData(@"C:\work", "work", @"C:\")]
    public void Row_is_the_folder_name_then_its_parent_with_home_as_a_tilde(string folder, string name, string parent)
    {
        var row = RecentFolderList.Row(folder, Home);

        Assert.Equal(name, row.Name);
        Assert.Equal(parent, row.Parent);
        Assert.Equal(folder, row.Path);
    }

    [Fact]
    public void Row_shortens_a_windows_home()
    {
        var row = RecentFolderList.Row(@"C:\Users\me\code\repo", @"C:\Users\me");

        Assert.Equal(@"~\code", row.Parent);
    }

    [Theory]
    [InlineData("SIDE", "sidebar-arrows")]
    [InlineData("tuicode", "sidebar-arrows,save-conflict")]
    [InlineData("~/code/key", "vault")]
    [InlineData("", "sidebar-arrows,save-conflict,vault")]
    [InlineData("nothing", "")]
    public void Filter_matches_the_name_or_the_parent_case_insensitively(string filter, string expected)
    {
        IReadOnlyList<RecentFolder> rows =
        [
            RecentFolderList.Row("/Users/me/code/TuiCode/sidebar-arrows", Home),
            RecentFolderList.Row("/Users/me/code/TuiCode/save-conflict", Home),
            RecentFolderList.Row("/Users/me/code/keyboards/vault", Home),
        ];

        var names = RecentFolderList.Filter(rows, filter).Select(r => r.Name);

        Assert.Equal(expected, string.Join(",", names));
    }

    [Fact]
    public void Display_lines_the_parents_up_in_a_column_after_the_names()
    {
        var row = RecentFolderList.Row("/Users/me/code/TuiCode/cli-path", Home);

        Assert.Equal("cli-path       ~/code/TuiCode", RecentFolderList.Display(row, 13, 40));
    }

    [Fact]
    public void Display_cuts_a_row_wider_than_the_list_with_an_ellipsis()
    {
        var row = RecentFolderList.Row("/Users/me/code/TuiCode/cli-path", Home);

        Assert.Equal("cli-path  ~/code/Tu…", RecentFolderList.Display(row, 8, 20));
    }
}
