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
    public async Task Enter_on_the_PR_header_opens_its_conversation_in_a_read_only_Markdown_tab()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.TitleText.Length > 0,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.HeaderHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveTab is { Title: "#183 Overview" });

        var tab = Assert.Single(workbench.Editor.Group.Tabs);
        Assert.True(tab.IsReadOnly);
        Assert.Equal("markdown", tab.Grammar?.Id);
        Assert.Equal(["# #183 Review tab shows the PR", "", "jamescrosswell · 2026-09-20 06:54", "", "What it does.", ""], tab.Lines);
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
            () => review.HeaderHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveTab is { Title: "#183 Overview" },
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.ListHasFocus,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.HeaderHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveTab is { Title: "#183 Overview" });

        Assert.Equal(["#183 Overview", "a.txt"], group.Tabs.Select(t => t.Title));
        Assert.Equal([183], _gitHub.ConversationCalls);
    }

    [Fact]
    public async Task What_gh_says_instead_of_the_conversation_is_shown_in_the_tab()
    {
        _gitHub.ConversationError = "GraphQL: Could not resolve to a PullRequest";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.TitleText.Length > 0,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.HeaderHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveTab is { Title: "#183 Overview" });

        Assert.Equal("GraphQL: Could not resolve to a PullRequest", workbench.Editor.Group.ActiveTab!.Lines[0]);
    }

    [Fact]
    public async Task A_read_only_tab_cant_be_typed_into_and_isnt_saved_over_the_file_list()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.TitleText.Length > 0,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.HeaderHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveTab is { Title: "#183 Overview" },
            () => host.App.InjectKey(new Key('x')),
            () => commands.TryExecute(CommandIds.SaveActiveEditor));

        var tab = workbench.Editor.Group.ActiveTab!;
        Assert.False(tab.IsDirty);
        Assert.StartsWith("# #183 ", tab.Lines[0]);
        Assert.False(_fs.File.Exists(_fs.Path.Combine(_git.Root!, "#183 Overview")));
    }

    [Fact]
    public async Task The_overview_can_be_searched_with_Ctrl_F_but_not_replaced_in()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.TitleText.Length > 0,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.HeaderHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveTab is { Title: "#183 Overview" },
            () => commands.TryExecute(CommandIds.ReplaceInFile),
            () => { foreach (var c in "does") host.App.InjectKey(new Key(c)); },
            () => workbench.Editor.Group.ActiveTab!.SelectedText == "does",
            () => host.App.InjectKey(Key.Tab),
            () => { foreach (var c in "did") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Enter));

        Assert.Equal("What it does.", workbench.Editor.Group.ActiveTab!.Lines[4]);
        Assert.False(workbench.Editor.Group.ActiveTab!.IsDirty);
    }

    [Fact]
    public async Task Without_a_PR_the_header_isnt_selectable()
    {
        _gitHub.PullRequest = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.ListHasFocus,
            () => host.App.InjectKey(Key.CursorUp));

        Assert.False(review.HeaderHasFocus);
        Assert.True(review.ListHasFocus);
        Assert.Empty(_gitHub.ConversationCalls);
    }

    [Fact]
    public async Task Down_from_the_header_goes_back_to_the_file_list()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.TitleText.Length > 0,
            () => host.App.InjectKey(Key.CursorUp),
            () => review.HeaderHasFocus,
            () => host.App.InjectKey(Key.CursorDown),
            () => review.ListHasFocus);

        Assert.Empty(workbench.Editor.Group.Tabs);
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
