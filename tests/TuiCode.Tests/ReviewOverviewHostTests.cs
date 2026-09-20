using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives the Overview tab (#185) through the host against a fake git and gh. Boots a TG Application — serialised (#77).
public class ReviewOverviewHostTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public ReviewOverviewHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        _gitHub.PullRequest = new GitHubPullRequest(183, "Review tab shows the PR", "main", "feature", default);
        _gitHub.Conversation = new GitHubConversation(183, "Review tab shows the PR", "jamescrosswell",
            new DateTimeOffset(2026, 9, 20, 6, 54, 0, TimeSpan.Zero), "What it does.", []);
    }

    [Fact]
    public async Task The_Overview_button_opens_the_PRs_conversation_as_a_document()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.TitleText.Length > 0,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.OverviewHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDocumentTab is { Title: "#183 Overview" });

        var tab = workbench.Editor.Group.ActiveDocumentTab!;
        Assert.Equal(
            """
            # #183 Review tab shows the PR

            jamescrosswell · 2026-09-20 06:54

            What it does.

            """,
            tab.Content.ReplaceLineEndings("\n"));
        Assert.Empty(workbench.Editor.Group.Tabs);
        Assert.Equal([183], _gitHub.ConversationCalls);
    }

    [Fact]
    public async Task Opening_the_overview_again_focuses_the_tab_it_already_has()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.TitleText.Length > 0,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.OverviewHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDocumentTab is { Title: "#183 Overview" },
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.ListHasFocus,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.OverviewHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDocumentTab is { Title: "#183 Overview" });

        Assert.Equal(["a.txt"], group.Tabs.Select(t => t.Title));
        Assert.Equal([183], _gitHub.ConversationCalls);
    }

    [Fact]
    public async Task The_pro_mnemonic_opens_the_overview_without_the_review_tab()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.PullRequestOverview),
            () => workbench.Editor.Group.ActiveDocumentTab is { Title: "#183 Overview" });

        Assert.Equal([183], _gitHub.ConversationCalls);
    }

    [Fact]
    public async Task Without_a_PR_the_pro_mnemonic_says_so()
    {
        _gitHub.PullRequest = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.PullRequestOverview),
            () => workbench.StatusBar.DisplayedText == "No pull request for this branch.");

        Assert.Null(workbench.Editor.Group.ActiveDocumentTab);
        Assert.Empty(_gitHub.ConversationCalls);
    }

    [Fact]
    public async Task What_gh_says_instead_of_the_conversation_is_shown_in_the_tab()
    {
        _gitHub.ConversationError = "GraphQL: Could not resolve to a PullRequest";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.PullRequestOverview),
            () => workbench.Editor.Group.ActiveDocumentTab is { Title: "#183 Overview" });

        Assert.Equal("GraphQL: Could not resolve to a PullRequest", workbench.Editor.Group.ActiveDocumentTab!.Content);
    }

    [Fact]
    public async Task A_document_cant_be_typed_into_and_isnt_saved_over_the_file_list()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.PullRequestOverview),
            () => workbench.Editor.Group.ActiveDocumentTab is { Title: "#183 Overview" },
            () => host.App.InjectKey(new Key('x')),
            () => commands.TryExecute(CommandIds.SaveActiveEditor));

        Assert.StartsWith("# #183 ", workbench.Editor.Group.ActiveDocumentTab!.Content);
        Assert.False(_fs.File.Exists(_fs.Path.Combine(_git.Root!, "#183 Overview")));
    }

    [Fact]
    public async Task Without_a_PR_there_is_no_Overview_button()
    {
        _gitHub.PullRequest = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.ListHasFocus,
            () => host.App.InjectKey(Key.CursorUp));

        Assert.False(review.OverviewHasFocus);
        Assert.True(review.ListHasFocus);
        Assert.Empty(_gitHub.ConversationCalls);
    }

    [Fact]
    public async Task Down_from_the_Overview_button_goes_back_to_the_file_list()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.TitleText.Length > 0,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.OverviewHasFocus,
            () => host.App.InjectKey(Key.CursorDown),
            () => review.ListHasFocus);

        Assert.Null(workbench.Editor.Group.ActiveDocumentTab);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        var workbench = new Workbench.Workbench(sidebar, new EditorPart(Syntax), new StatusBarPart());
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
