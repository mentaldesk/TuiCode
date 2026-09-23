using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `cc` on a thread (#189) through the host against a fake git and gh. Boots a TG Application — serialised (#77).
public class ReplyToThreadHostTests : StaticConfigurationTest
{
    private const long ThreadId = 4711;
    private static readonly DateTimeOffset Posted = new(2026, 9, 20, 6, 54, 0, TimeSpan.Zero);

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public ReplyToThreadHostTests()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("alpha\nbravo\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.txt")];
        _git.RepoFiles["b45e:src/a.txt"] = "alpha\nBRAVO\n";
        _gitHub.PullRequest = new GitHubPullRequest(189, "Replies", "main", "feature", default);
        _gitHub.ReviewThreads = [Thread(2, "Does this hold?")];
    }

    [Fact]
    public async Task Cc_on_a_thread_row_posts_the_reply_and_the_thread_shows_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OnThreadRow(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => Type(host, "It does now."),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

        Assert.Equal((_git.Root, 189, ThreadId, "It does now."), _gitHub.Replies.Single());
        var thread = workbench.Editor.Group.ActiveDiffTab!.CurrentThread!;
        Assert.Equal(1, thread.Replies);
        Assert.Equal("It does now.", thread.Comments[^1].Body);
        Assert.StartsWith("Replied on #189", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task The_review_tab_shows_the_reply_without_the_threads_being_fetched_again()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OnThreadRow(host, workbench, commands);
        await Reply(host, workbench, commands, "It does now.");

        Assert.Equal(1, workbench.Sidebar.Review.Review!.ThreadsOn("src/a.txt").Single().Replies);
    }

    [Fact]
    public async Task The_dialog_says_who_is_being_replied_to_and_offers_reply_or_cancel()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var title = "";
        string[] buttons = [];

        await OnThreadRow(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () =>
            {
                title = Dialog(workbench)!.Title;
                buttons = [.. Dialog(workbench)!.SubViews.OfType<Button>().Select(b => b.Text)];
            },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("Reply to octocat", title);
        Assert.Equal(["Reply", "Cancel"], buttons);
        Assert.Empty(_gitHub.Replies);
    }

    [Fact]
    public async Task A_reply_GitHub_refuses_keeps_the_dialog_and_what_was_typed()
    {
        _gitHub.ReplyError = "HTTP 422: Unprocessable Entity";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OnThreadRow(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => Type(host, "It does now."),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench)!.Status == _gitHub.ReplyError);

        Assert.Equal("It does now.", Dialog(workbench)!.Body);
        Assert.Equal(0, workbench.Editor.Group.ActiveDiffTab!.CurrentThread!.Replies);
    }

    [Fact]
    public async Task A_reply_without_a_body_says_so_and_posts_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OnThreadRow(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench)!.Status == "A reply needs something to say.",
            () => host.App.InjectKey(Key.Esc));

        Assert.Empty(_gitHub.Replies);
    }

    [Fact]
    public async Task A_thread_GitHub_gave_no_reply_target_for_says_so()
    {
        _gitHub.ReviewThreads = [Thread(2, "Does this hold?", id: 0)];
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OnThreadRow(host, workbench, commands);
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => workbench.StatusBar.DisplayedText.StartsWith("GitHub didn't say what to reply to on this thread"));

        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task A_resolved_thread_takes_a_reply_like_any_other()
    {
        _gitHub.ReviewThreads = [Thread(2, "Settled?", resolved: true)];
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await OnThreadRow(host, workbench, commands);
        await Reply(host, workbench, commands, "Reopening this.");

        Assert.Equal("Reopening this.", _gitHub.Replies.Single().Body);
        Assert.True(workbench.Editor.Group.ActiveDiffTab!.CurrentThread!.Resolved);
    }

    [Fact]
    public async Task Cc_replies_to_an_outdated_thread_selected_in_the_review_tab()
    {
        _gitHub.ReviewThreads = [Thread(null, "Whatever happened to this?", id: 99)];
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.ThreadsText.Length > 0,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => review.SelectedThread is not null);
        await Reply(host, workbench, commands, "It went in #12.");

        Assert.Equal((_git.Root, 189, 99L, "It went in #12."), _gitHub.Replies.Single());
        Assert.Equal(1, review.SelectedThread!.Replies);
        Assert.True(review.ListHasFocus);
    }

    /// <summary>Opens the review's only file as a diff and steps onto the thread under the changed line.</summary>
    private static async Task OnThreadRow(WorkbenchHost host, Workbench.Workbench workbench, CommandService commands)
    {
        var group = workbench.Editor.Group;
        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab?.Threads.Count == 1,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => group.ActiveDiffTab!.CurrentThread is not null);
    }

    private static Task Reply(WorkbenchHost host, Workbench.Workbench workbench, CommandService commands, string body) =>
        HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CreateComment),
            () => Dialog(workbench) is not null,
            () => Type(host, body),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

    private static GitHubReviewThread Thread(int? line, string body, bool resolved = false, long id = ThreadId) =>
        new("src/a.txt", line, resolved, line is null, [new GitHubComment("octocat", Posted, body)], id);

    private static CommentView? Dialog(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<CommentView>().SingleOrDefault();

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
