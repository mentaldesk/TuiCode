using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Tests;

public class WorktreeListTests
{
    private const string Home = "/Users/ann";

    private static readonly GitWorktree[] Worktrees =
    [
        new("/Users/ann/code/TuiCode/main", "main", "aaaaaaa111"),
        new("/Users/ann/code/TuiCode/save-conflict", "fix/save-conflict", "bbbbbbb222"),
        new("/Users/ann/code/TuiCode/pr-247", null, "3f9c2e1abc"),
        new("/srv/builds/cli-path", "feat/cli-path", "ccccccc333"),
    ];

    [Fact]
    public void Rows_keep_git_s_order_and_leave_out_the_worktree_you_are_in()
    {
        var rows = WorktreeList.Rows(Worktrees, "/Users/ann/code/TuiCode/save-conflict/", Home);

        Assert.Equal(["main", "pr-247", "cli-path"], rows.Select(r => r.Name));
    }

    [Fact]
    public void Row_names_the_branch_or_the_detached_short_hash_and_shortens_home_in_the_parent()
    {
        var rows = WorktreeList.Rows(Worktrees, "/elsewhere", Home);

        Assert.Equal(new WorktreeRow("/Users/ann/code/TuiCode/main", "main", "main", "~/code/TuiCode"), rows[0]);
        Assert.Equal(new WorktreeRow("/Users/ann/code/TuiCode/pr-247", "(detached 3f9c2e1)", "pr-247", "~/code/TuiCode"), rows[2]);
        Assert.Equal(new WorktreeRow("/srv/builds/cli-path", "feat/cli-path", "cli-path", "/srv/builds"), rows[3]);
    }

    [Fact]
    public void Row_of_a_worktree_directly_in_home_has_a_parent_of_tilde()
    {
        Assert.Equal("~", WorktreeList.Row(new GitWorktree("/Users/ann/repo", "main"), Home).Parent);
        Assert.Equal("/Users/anna", WorktreeList.Row(new GitWorktree("/Users/anna/repo", "main"), Home).Parent);
    }

    [Fact]
    public void Display_lines_up_the_branch_name_and_parent_columns()
    {
        var row = new WorktreeRow("/x/y/main", "main", "main", "~/x");

        Assert.Equal("main                main          ~/x", WorktreeList.Display(row, 18, 12, 60));
    }

    [Fact]
    public void Display_caps_each_column_at_a_third_of_the_row_and_truncates_the_rest()
    {
        var row = new WorktreeRow("/p/a-long-folder-name", "feature/a-very-long-branch-name", "a-long-folder-name", "~/somewhere/deep");

        Assert.Equal("feature/…  a-long-f…  ~/somewher…", WorktreeList.Display(row, 40, 40, 33));
    }

    [Fact]
    public void Header_names_each_column_lined_up_with_the_rows()
    {
        Assert.Equal("Branch              Worktree      Location", WorktreeList.Header(18, 12, 60));
    }
}
