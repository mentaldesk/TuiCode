using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `cc` (#188) through the host against a fake git and gh. Boots a TG Application — serialised (#77).
public class CreateCommentHostTests : StaticConfigurationTest
{
    private const string Head = "e11a";

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public CreateCommentHostTests()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("alpha\nbravo\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.txt")];
        _git.RepoFiles["b45e:src/a.txt"] = "alpha\nBRAVO\n";
        _git.RepoFiles[$"{Head}:src/a.txt"] = "alpha\nbravo\n";
        _gitHub.PullRequest = new GitHubPullRequest(188, "Comments", "main", "feature", default, Head);
    }

    [Fact]
    public async Task Cc_drafts_a_comment_on_the_current_line_and_shows_it_under_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OpenDiff(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => Type(host, "Rename this?"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

        var diff = workbench.Editor.Group.ActiveDiffTab!;
        Assert.Equal([new DraftComment("src/a.txt", 1, "Rename this?")], diff.Drafts);
        Assert.Equal("Draft review: 1 comment", workbench.Sidebar.Review.DraftReviewText);
    }

    [Fact]
    public async Task The_comment_box_shows_it_has_focus_as_soon_as_the_dialog_opens()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var shows = false;

        await OpenDiff(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => { shows = Dialog(workbench)!.SubViews.OfType<InputView>().Single().ShowsFocus; },
            () => host.App.InjectKey(Key.Esc));

        Assert.True(shows);
    }

    [Fact]
    public async Task The_dialog_is_titled_with_the_file_and_the_line_it_comments_on()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var title = "";

        await OpenDiff(host, workbench, commands);
        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.CursorDown),
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => { title = Dialog(workbench)!.Title; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("Comment on src/a.txt:2", title);
    }

    [Fact]
    public async Task A_draft_is_back_after_restarting()
    {
        using (var workbench = BuildWorkbench())
        using (var host = BuildHost(workbench, out var commands))
        {
            await OpenDiff(host, workbench, commands);
            await HostSteps.Run(host,
                () => commands.TryExecute(CommandIds.CreateComment),
                () => Dialog(workbench) is not null,
                () => Type(host, "Rename this?"),
                () => host.App.InjectKey(Key.Enter.WithCtrl),
                () => Dialog(workbench) is null);
        }

        using var reopened = BuildWorkbench();
        using var second = BuildHost(reopened, out var reopenedCommands);
        await OpenDiff(second, reopened, reopenedCommands);

        Assert.Equal(["Rename this?"], reopened.Editor.Group.ActiveDiffTab!.Drafts.Select(d => d.Body));
        Assert.Equal("Draft review: 1 comment", reopened.Sidebar.Review.DraftReviewText);
    }

    [Fact]
    public async Task Enter_on_a_draft_row_edits_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OpenDiff(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => Type(host, "Rename this?"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null,
            () => host.App.InjectKey(Key.CursorDown),
            () => workbench.Editor.Group.ActiveDiffTab!.CurrentDraft is not null,
            () => host.App.InjectKey(Key.Enter),
            () => Dialog(workbench) is not null,
            () => Assert.Equal("Rename this?", Dialog(workbench)!.Body),
            () => Type(host, " Or drop it?"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

        Assert.Equal(
            [new DraftComment("src/a.txt", 1, "Rename this? Or drop it?")],
            workbench.Editor.Group.ActiveDiffTab!.Drafts);
    }

    [Fact]
    public async Task Delete_takes_the_draft_away()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OpenDiff(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => Type(host, "Rename this?"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null,
            () => host.App.InjectKey(Key.CursorDown),
            () => workbench.Editor.Group.ActiveDiffTab!.CurrentDraft is not null,
            () => host.App.InjectKey(Key.Enter),
            () => Dialog(workbench) is not null,
            () => Click(Button(workbench, "Delete")),
            () => Dialog(workbench) is null);

        Assert.Empty(workbench.Editor.Group.ActiveDiffTab!.Drafts);
        Assert.Equal("", workbench.Sidebar.Review.DraftReviewText);
    }

    [Fact]
    public async Task A_comment_without_a_body_says_so_and_drafts_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OpenDiff(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench)!.Status == "A comment needs something to say.",
            () => host.App.InjectKey(Key.Esc));

        Assert.Empty(workbench.Editor.Group.ActiveDiffTab!.Drafts);
    }

    [Fact]
    public async Task On_a_line_only_the_base_has_cc_says_to_comment_on_the_right()
    {
        _git.RepoFiles["b45e:src/a.txt"] = "alpha\ngone\nbravo\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OpenDiff(host, workbench, commands);
        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.CursorDown),
            () => workbench.Editor.Group.ActiveDiffTab!.CurrentHeadLine is null,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => workbench.StatusBar.DisplayedText == "Comment on a line on the right");

        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task A_file_that_differs_from_the_pull_requests_head_takes_no_comment()
    {
        _git.RepoFiles[$"{Head}:src/a.txt"] = "alpha\nbravo\ncharlie\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OpenDiff(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => workbench.StatusBar.DisplayedText
                == "This file differs from #188's head: comments would land on the wrong lines");

        Assert.Null(Dialog(workbench));
        Assert.Empty(workbench.Editor.Group.ActiveDiffTab!.Drafts);
    }

    [Fact]
    public async Task Outside_a_review_diff_cc_says_where_to_comment_from()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => workbench.StatusBar.DisplayedText == "Open a file from the Review tab to comment");

        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task Sr_posts_the_drafts_with_the_review_and_clears_them()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OpenDiff(host, workbench, commands);
        await Draft(host, workbench, commands, "Rename this?");
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Submit(workbench) is not null,
            () => Assert.Equal("1 draft comment will be posted with it.", Submit(workbench)!.DraftsText),
            () => Type(host, "Nearly there"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Submit(workbench) is null);

        Assert.Equal([new DraftComment("src/a.txt", 1, "Rename this?")], _gitHub.ReviewComments.Single());
        Assert.Empty(workbench.Editor.Group.ActiveDiffTab!.Drafts);
        Assert.Equal("", workbench.Sidebar.Review.DraftReviewText);
    }

    [Fact]
    public async Task A_refused_review_keeps_the_drafts()
    {
        _gitHub.ReviewError = "GraphQL: Can not approve your own pull request";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OpenDiff(host, workbench, commands);
        await Draft(host, workbench, commands, "Rename this?");
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Submit(workbench) is not null,
            () => Type(host, "Nearly there"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Submit(workbench)!.Status == _gitHub.ReviewError,
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal([new DraftComment("src/a.txt", 1, "Rename this?")], workbench.Editor.Group.ActiveDiffTab!.Drafts);
        Assert.Equal("Draft review: 1 comment", workbench.Sidebar.Review.DraftReviewText);
    }

    /// <summary>Opens the review's only file as a diff, with its threads and drafts in.</summary>
    private static Task OpenDiff(WorkbenchHost host, Workbench.Workbench workbench, CommandService commands) =>
        HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is not null);

    private static Task Draft(WorkbenchHost host, Workbench.Workbench workbench, CommandService commands, string body) =>
        HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => Type(host, body),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

    private static CommentView? Dialog(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<CommentView>().SingleOrDefault();

    private static SubmitReviewView? Submit(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<SubmitReviewView>().SingleOrDefault();

    private static Button Button(Workbench.Workbench workbench, string text) =>
        Dialog(workbench)!.SubViews.OfType<Button>().Single(button => button.Text == text);

    private static void Click(View view) =>
        view.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonClicked, Position = new System.Drawing.Point(1, 0) });

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        var workbench = new Workbench.Workbench(sidebar, new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
    }
}
