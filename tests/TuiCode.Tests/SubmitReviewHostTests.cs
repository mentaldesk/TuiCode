using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using MentalDesk.Tui.Loading;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Controls;
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
            () => Dialog(workbench) is { Loaded: true },
            () => Type(host, "Looks good"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

        Assert.Equal([(_git.Root, 187, GitHubReviewVerdict.Comment, "Looks good")], _gitHub.Reviews);
        Assert.Equal("Review submitted on #187", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task The_summary_box_shows_it_has_focus_as_soon_as_the_dialog_opens()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var shows = false;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is { Loaded: true },
            () => { shows = Dialog(workbench)!.SummaryBox.ShowsFocus; },
            () => host.App.InjectKey(Key.Esc));

        Assert.True(shows);
    }

    [Fact]
    public async Task The_dialog_is_titled_with_the_branchs_pull_request()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var title = "";

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is { Loaded: true },
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
            () => Dialog(workbench) is { Loaded: true },
            () => Choose(host, GitHubReviewVerdict.Approve),
            () => Dialog(workbench)!.Verdict == GitHubReviewVerdict.Approve,
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench) is null);

        Assert.Equal([(_git.Root, 187, GitHubReviewVerdict.Approve, "")], _gitHub.Reviews);
    }

    [Fact]
    public async Task Tabbing_back_into_the_verdict_lands_on_the_chosen_one_and_one_Tab_leaves()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var summaryFocused = false;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is { Loaded: true },
            () => Choose(host, GitHubReviewVerdict.Approve),
            () => host.App.InjectKey(Key.Tab.WithShift),
            () => host.App.InjectKey(Key.CursorRight),
            () => Dialog(workbench)!.Verdict == GitHubReviewVerdict.RequestChanges,
            () => host.App.InjectKey(Key.Tab),
            () => { summaryFocused = Dialog(workbench)!.SubViews.SelectMany(Descendants).OfType<InputView>().Single().HasFocus; },
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null);

        Assert.True(summaryFocused);
    }

    private static IEnumerable<View> Descendants(View view) => view.SubViews.SelectMany(Descendants).Prepend(view);

    [Theory]
    [InlineData(GitHubReviewVerdict.Comment)]
    [InlineData(GitHubReviewVerdict.RequestChanges)]
    public async Task Commenting_and_requesting_changes_without_a_summary_says_so_and_submits_nothing(GitHubReviewVerdict verdict)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is { Loaded: true },
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
            () => Dialog(workbench) is { Loaded: true },
            () => Type(host, "Ship it"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench)!.Status == "GraphQL: Can not approve your own pull request",
            () => { summary = Dialog(workbench)!.Summary; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("Ship it", summary);
        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task A_refusal_too_long_for_one_row_wraps_under_the_buttons_rather_than_over_them()
    {
        _gitHub.ReviewError = "failed to create review: GraphQL: Can not approve your own pull request (addPullRequestReview)";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var alert = System.Drawing.Rectangle.Empty;
        var cancel = System.Drawing.Rectangle.Empty;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is { Loaded: true },
            () => Type(host, "Ship it"),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Dialog(workbench)!.Status == _gitHub.ReviewError,
            () => Alert(workbench).Frame.Height > 1,
            () => { alert = Alert(workbench).Frame; cancel = Button(workbench, "Esc Cancel").Frame; },
            () => host.App.InjectKey(Key.Esc));

        Assert.True(alert.Height > 1, $"the refusal took {alert.Height} row(s), so it never wrapped");
        Assert.Equal(cancel.Y + 1, alert.Y);
    }

    [Fact]
    public async Task Esc_cancels_without_submitting_anything()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is { Loaded: true },
            () => Type(host, "Never mind"),
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null);

        Assert.Empty(_gitHub.Reviews);
    }

    [Fact]
    public async Task Sr_opens_at_once_and_shows_the_form_once_gh_has_found_the_pull_request()
    {
        var gate = new TaskCompletionSource();
        _gitHub.HoldPullRequest = gate;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var stateWhileLoading = LoadState.Loaded;
        var titleWhileLoading = "";

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () =>
            {
                stateWhileLoading = Dialog(workbench)!.State.State;
                titleWhileLoading = Dialog(workbench)!.Title;
                host.App.InjectKey(Key.Enter.WithCtrl);
                gate.SetResult();
            },
            () => Dialog(workbench) is { Loaded: true },
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null);

        Assert.Equal(LoadState.Loading, stateWhileLoading);
        Assert.Equal("Submit review", titleWhileLoading);
        Assert.Empty(_gitHub.Reviews);
        Assert.Equal(workbench.StatusBar.DefaultMessage, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Esc_closes_the_dialog_while_it_loads_and_the_late_answer_opens_nothing()
    {
        var gate = new TaskCompletionSource();
        _gitHub.HoldPullRequest = gate;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var lookup = Task.CompletedTask;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null,
            () =>
            {
                gate.SetResult();
                lookup = Task.Delay(200);
            },
            () => lookup.IsCompleted,
            () => Assert.Null(Dialog(workbench)));

        Assert.Equal(workbench.StatusBar.DefaultMessage, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Without_a_pull_request_the_dialog_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var says = "";

        await HostSteps.Run(host,
            () => { _gitHub.PullRequest = null; },
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench)?.State.State == LoadState.Empty,
            () => { says = Dialog(workbench)!.State.Says; },
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null);

        Assert.Equal("No pull request for this branch", says);
        Assert.Equal(workbench.StatusBar.DefaultMessage, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Without_gh_the_dialog_says_so_and_R_tries_again()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var says = "";

        await HostSteps.Run(host,
            () => { _gitHub.Missing = true; },
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench)?.State.State == LoadState.Failed,
            () =>
            {
                says = Dialog(workbench)!.State.Says;
                _gitHub.Missing = false;
                host.App.InjectKey(Key.R);
            },
            () => Dialog(workbench) is { Loaded: true },
            () => Assert.Equal("", Dialog(workbench)!.Summary),
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null);

        Assert.Equal("Pull requests need the GitHub CLI: run gh auth login", says);
    }

    [Fact]
    public async Task Outside_a_git_repository_the_dialog_says_so_and_offers_no_retry()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var says = "";

        await HostSteps.Run(host,
            () => { _git.Root = null; },
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench)?.State.State == LoadState.Empty,
            () =>
            {
                says = Dialog(workbench)!.State.Says;
                host.App.InjectKey(Key.R);
            },
            () => Assert.Equal(LoadState.Empty, Dialog(workbench)!.State.State),
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null);

        Assert.Equal($"{_fs.Path.GetFullPath("/work")} isn't in a git repository", says);
    }

    [Fact]
    public async Task Clicking_Submit_posts_the_review()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is { Loaded: true },
            () => Type(host, "Looks good"),
            () => Click(Button(workbench, "Ctrl+Enter Submit")),
            () => Dialog(workbench) is null);

        Assert.Equal([(_git.Root, 187, GitHubReviewVerdict.Comment, "Looks good")], _gitHub.Reviews);
    }

    [Fact]
    public async Task Clicking_Cancel_closes_the_dialog_without_submitting()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.SubmitReview),
            () => Dialog(workbench) is { Loaded: true },
            () => Type(host, "Never mind"),
            () => Click(Button(workbench, "Esc Cancel")),
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
            () => Dialog(workbench) is { Loaded: true },
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
        host.App.InjectKey(Key.Tab);
    }

    private static AlertView Alert(Workbench.Workbench workbench) =>
        Dialog(workbench)!.SubViews.OfType<AlertView>().Single();

    private static Button Button(Workbench.Workbench workbench, string text) =>
        Dialog(workbench)!.SubViews.OfType<Button>().Single(button => button.Text.Trim() == text);

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
