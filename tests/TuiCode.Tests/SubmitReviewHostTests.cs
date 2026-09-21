using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `sr` through the host against a fake git and gh. Boots a TG Application — serialised (#77).
public class SubmitReviewHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public SubmitReviewHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _gitHub.PullRequest = new GitHubPullRequest(187, "Submit review", "main", "milestone-187", default);
    }

    [Fact]
    public async Task Sr_submits_a_comment_with_its_summary_and_says_so_in_the_status_bar()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => Type(host, "Looks good"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

        Assert.Equal([(_git.Root, 187, GitHubReviewVerdict.Comment, "Looks good")], _gitHub.Reviews);
        Assert.Equal("Review submitted on #187", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task The_dialog_is_titled_with_the_branchs_pull_request()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var title = "";

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => { title = Dialog(workbench)!.Title; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("Submit review on #187", title);
    }

    [Fact]
    public async Task Approving_needs_no_summary()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => Choose(host, GitHubReviewVerdict.Approve),
            () => Dialog(workbench)!.Verdict == GitHubReviewVerdict.Approve,
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

        Assert.Equal([(_git.Root, 187, GitHubReviewVerdict.Approve, "")], _gitHub.Reviews);
    }

    [Theory]
    [InlineData(GitHubReviewVerdict.Comment)]
    [InlineData(GitHubReviewVerdict.RequestChanges)]
    public async Task Commenting_and_requesting_changes_without_a_summary_says_so_and_submits_nothing(GitHubReviewVerdict verdict)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => Choose(host, verdict),
            () => Dialog(workbench)!.Verdict == verdict,
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench)!.Status == "Comment and Request changes need a summary.",
            () => host.App.InjectKey(Key.Esc));

        Assert.Empty(_gitHub.Reviews);
    }

    [Fact]
    public async Task A_refused_review_is_shown_in_the_dialog_which_stays_open_with_its_text()
    {
        _gitHub.ReviewError = "GraphQL: Can not approve your own pull request";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var summary = "";

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => Type(host, "Ship it"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench)!.Status == "GraphQL: Can not approve your own pull request",
            () => { summary = Dialog(workbench)!.Summary; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("Ship it", summary);
        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task Esc_cancels_without_submitting_anything()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => Type(host, "Never mind"),
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null);

        Assert.Empty(_gitHub.Reviews);
    }

    [Fact]
    public async Task Without_a_pull_request_sr_says_so_in_the_status_bar_and_opens_nothing()
    {
        _gitHub.PullRequest = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => workbench.StatusBar.DisplayedText == "No pull request for this branch.");

        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task Without_gh_sr_says_so_in_the_status_bar_and_opens_nothing()
    {
        _gitHub.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => workbench.StatusBar.DisplayedText == "Pull requests need the GitHub CLI: run gh auth login");

        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task Clicking_the_submit_hint_posts_the_review()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => Type(host, "Looks good"),
            () => Click(Hint(workbench, "Ctrl+Enter submit")),
            () => Dialog(workbench) is null);

        Assert.Equal([(_git.Root, 187, GitHubReviewVerdict.Comment, "Looks good")], _gitHub.Reviews);
    }

    [Fact]
    public async Task Clicking_the_cancel_hint_closes_the_dialog_without_submitting()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => Type(host, "Never mind"),
            () => Click(Hint(workbench, "Esc cancel")),
            () => Dialog(workbench) is null);

        Assert.Empty(_gitHub.Reviews);
    }

    [Fact]
    public async Task A_summary_too_long_for_the_dialog_wraps_but_is_posted_as_one_line()
    {
        var summary = string.Join(' ', Enumerable.Repeat("summary", 20));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => Type(host, summary),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

        Assert.Equal([(_git.Root, 187, GitHubReviewVerdict.Comment, summary)], _gitHub.Reviews);
    }

    private static SubmitReviewView? Dialog(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<SubmitReviewView>().SingleOrDefault();

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    /// <summary>Tabs back to the verdicts, steps right to <paramref name="verdict"/> and selects it.</summary>
    private static void Choose(WorkbenchHost host, GitHubReviewVerdict verdict)
    {
        host.App.InjectKey(Key.Tab.WithShift);
        for (var i = 0; i < (int)verdict; i++) host.App.InjectKey(Key.CursorRight);
        host.App.InjectKey(Key.Space);
        host.App.InjectKey(Key.Tab);
    }

    private static Button Hint(Workbench.Workbench workbench, string text) =>
        Dialog(workbench)!.SubViews.OfType<Button>().Single(button => button.Text == text);

    private static void Click(View view) =>
        view.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonClicked, Position = new System.Drawing.Point(1, 0) });

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
