using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// F1's right column in the Review tab (#430), against a fake git and gh. Boots a TG Application — serialised (#77).
public class HelpReviewHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();
    private readonly CommandService _commands = new();

    public HelpReviewHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        _gitHub.PullRequest = new GitHubPullRequest(430, "Help", "main", "feature", default, Id: "PR_kw430");
    }

    [Fact]
    public async Task F1_in_the_review_list_lists_its_keys()
    {
        var help = await HelpFrom(review => review.ListHasFocus && review.ViewedText.Length > 0);

        Assert.Equal("Review", help.Title);
        Assert.Equal(
        [
            new("Enter", "Open the file's diff"),
            new("↑", "To Overview"),
            new("Space", "Mark viewed or not"),
        ], help.Rows);
    }

    [Fact]
    public async Task Without_a_PR_up_to_Overview_isnt_listed()
    {
        _gitHub.PullRequest = null;
        var help = await HelpFrom(review => review.ListHasFocus && review.ViewedText.Length > 0);

        Assert.DoesNotContain(help.Rows, row => row.Description == "To Overview");
        Assert.Contains(new HelpRow("Enter", "Open the file's diff"), help.Rows);
    }

    [Fact]
    public async Task The_review_column_follows_a_rebind_of_toggle_viewed()
    {
        var help = await HelpFrom(review => review.ListHasFocus && review.ViewedText.Length > 0,
            settings: s => s.SetKeybindingOverrides([
                new(TestKeys.Chord("Space"), "-" + CommandIds.ToggleViewed),
                new(TestKeys.Chord("F6"), CommandIds.ToggleViewed),
            ]));

        Assert.Contains(new HelpRow("F6", "Mark viewed or not"), help.Rows);
    }

    [Fact]
    public async Task F1_on_the_Overview_row_lists_its_keys()
    {
        var help = await HelpFrom(review => review.TitleText.Length > 0, onOverview: true);

        Assert.Equal("Review", help.Title);
        Assert.Equal([new HelpRow("Enter", "Open the Overview"), new HelpRow("↓", "To the files")], help.Rows);
    }

    private async Task<HelpColumn> HelpFrom(Func<ReviewView, bool> ready, bool onOverview = false, Action<InMemorySettingsService>? settings = null)
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        using var workbench = new Workbench.Workbench(sidebar, new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        var inMemory = new InMemorySettingsService();
        settings?.Invoke(inMemory);
        using var host = new WorkbenchHost(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            inMemory, driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
        var review = workbench.Sidebar.Review;
        HelpView? view = null;

        await HostSteps.Run(host,
            () => _commands.TryExecute(CommandIds.FocusReview),
            () => ready(review),
            () => { if (onOverview) host.App.InjectKey(Key.CursorUp); },
            () => review.OverviewHasFocus == onOverview,
            () => host.App.InjectKey(Key.F1),
            () => (view = workbench.SubViews.OfType<HelpView>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));

        return view!.Place!;
    }
}
