using Terminal.Gui.Drawing;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Review;

namespace TuiCode.Tests;

public class ReviewThreadRowsTests
{
    private static readonly DateTimeOffset Posted = new(2026, 9, 20, 6, 54, 0, TimeSpan.Zero);

    [Fact]
    public void A_collapsed_thread_is_its_first_line_and_the_number_of_replies()
    {
        var thread = Thread(("octocat", "Does Esc stay global here?\nOr not?"), ("hubot", "It does."), ("octocat", "Thanks."));

        Assert.Equal(
            [new ThreadRow("┃ octocat: Does Esc stay global here?", "(2 replies)")],
            ReviewThreadRows.For(thread, expanded: false));
    }

    [Fact]
    public void A_thread_nobody_has_replied_to_says_nothing_about_replies()
    {
        var rows = ReviewThreadRows.For(Thread(("octocat", "Just this.")), expanded: false);

        Assert.Null(Assert.Single(rows).Trailing);
    }

    [Fact]
    public void One_reply_is_counted_in_the_singular()
    {
        var rows = ReviewThreadRows.For(Thread(("octocat", "Why?"), ("hubot", "Because.")), expanded: false);

        Assert.Equal("(1 reply)", Assert.Single(rows).Trailing);
    }

    [Fact]
    public void An_expanded_thread_is_every_comment_headed_by_its_author_and_date()
    {
        var thread = Thread(("octocat", "Does Esc stay global?\r\nIt matters."), ("hubot", "It does."));

        var rows = ReviewThreadRows.For(thread, expanded: true);

        Assert.Equal(
        [
            "┃ octocat · 2026-09-20 06:54",
            "┃ Does Esc stay global?",
            "┃ It matters.",
            "┃",
            "┃ hubot · 2026-09-20 06:54",
            "┃ It does.",
        ], rows.Select(r => r.Text));
    }

    private static GitHubReviewThread Thread(params (string Author, string Body)[] comments) =>
        new("src/a.cs", 2, Resolved: false, Outdated: false,
            [.. comments.Select(c => new GitHubComment(c.Author, Posted, c.Body))]);
}

public class ReviewThreadCountTests
{
    private static readonly GitChange Changed = new(GitChangeKind.Modified, "src/a.cs");

    [Fact]
    public void The_header_counts_the_threads_and_the_unresolved_ones()
    {
        Assert.Equal("", Review().ThreadsLine);
        Assert.Equal("1 thread", Review(Thread("src/a.cs", resolved: true)).ThreadsLine);
        Assert.Equal("2 threads, 1 unresolved", Review(Thread("src/a.cs", resolved: true), Thread("src/a.cs")).ThreadsLine);
    }

    [Fact]
    public void Threads_are_looked_up_by_the_file_they_are_on()
    {
        var review = Review(Thread("src/a.cs"), Thread("b.cs"), Thread("src/a.cs", resolved: true));

        Assert.Equal(2, review.ThreadsOn("src/a.cs").Count);
        Assert.Empty(review.ThreadsOn("src/gone.cs"));
    }

    [Fact]
    public void A_file_carries_its_threads_and_the_outdated_ones_hang_under_it()
    {
        var nodes = ReviewTree.Build([Changed], [Thread("src/a.cs"), Thread("src/a.cs", outdated: true)]);

        var file = Assert.IsType<ReviewFileNode>(Assert.Single(Assert.IsType<ReviewFolderNode>(Assert.Single(nodes)).Children));
        Assert.Equal(2, file.Threads.Count);
        var outdated = Assert.IsType<ReviewOutdatedNode>(Assert.Single(file.Children));
        Assert.Equal("! 1 outdated thread", outdated.ToString());
    }

    [Fact]
    public void A_file_with_no_threads_has_no_badge_and_no_children()
    {
        var file = Assert.IsType<ReviewFileNode>(Assert.Single(Assert.IsType<ReviewFolderNode>(Assert.Single(ReviewTree.Build([Changed]))).Children));

        Assert.Empty(file.Threads);
        Assert.Empty(file.Children);
        Assert.Equal("M a.cs", ReviewRow.Display(file, 20));
    }

    [Fact]
    public void A_files_thread_badge_is_pushed_to_the_right_of_the_row()
    {
        var nodes = ReviewTree.Build([Changed], [Thread("src/a.cs"), Thread("src/a.cs")]);
        var file = Assert.IsType<ReviewFolderNode>(Assert.Single(nodes)).Children[0];

        Assert.Equal("M a.cs           ● 2", ReviewRow.Display(file, 20));
    }

    [Fact]
    public void A_files_badge_counts_the_threads_still_open_and_marks_the_settled_ones_apart()
    {
        Assert.Equal("● 1", File(Thread("src/a.cs"), Thread("src/a.cs", resolved: true)).Badge);
        Assert.Equal("○ 2", File(Thread("src/a.cs", resolved: true), Thread("src/a.cs", resolved: true)).Badge);
        Assert.Null(File().Badge);
    }

    [Fact]
    public void An_outdated_thread_still_counts_towards_the_files_badge()
    {
        Assert.Equal("● 1", File(Thread("src/a.cs", outdated: true)).Badge);
    }

    [Fact]
    public void A_badge_is_bold_while_a_thread_is_open_and_faint_once_they_are_all_resolved()
    {
        Assert.Equal(TextStyle.Bold, File(Thread("src/a.cs"), Thread("src/a.cs", resolved: true)).BadgeStyle);
        Assert.Equal(TextStyle.Faint, File(Thread("src/a.cs", resolved: true)).BadgeStyle);
    }

    private static ReviewFileNode File(params GitHubReviewThread[] threads) =>
        Assert.IsType<ReviewFileNode>(Assert.Single(Assert.IsType<ReviewFolderNode>(Assert.Single(ReviewTree.Build([Changed], threads))).Children));

    private static BranchReview Review(params GitHubReviewThread[] threads) =>
        new("/work", "feature", "main", "b45e", [Changed], new GitHubPullRequest(186, "Threads", "main", "feature", default))
        {
            Threads = threads,
        };

    private static GitHubReviewThread Thread(string path, bool resolved = false, bool outdated = false) =>
        new(path, outdated ? null : 2, resolved, outdated, [new GitHubComment("octocat", default, "Look here.")]);
}
