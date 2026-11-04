using MentalDesk.Tui;
using MentalDesk.Tui.Shell;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// The terminal tab names the place TuiCode is open on (#658). Boots a TG Application — serialised (#77).
public class TerminalTitleHostTests : StaticConfigurationTest
{
    private const string Push = "\e[22;0t";
    private const string Pop = "\e[23;0t";

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly CommandService _commands = new();
    private readonly List<string> _written = [];

    public TerminalTitleHostTests()
    {
        _fs.AddFile("/code/TuiCode/main/a.txt", new MockFileData("a\n"));
        _fs.AddFile("/code/tui-style-guide/b.txt", new MockFileData("b\n"));
    }

    private IEnumerable<string> Titles => _written.Select(s => s.StartsWith("\e]0;", StringComparison.Ordinal) ? s[4..^1] : s);

    [Fact]
    public async Task Names_the_repo_and_folder_follows_a_change_of_folder_and_gives_the_terminal_its_title_back()
    {
        _git.OriginUrl = "git@github.com:mentaldesk/TuiCode.git";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/code/TuiCode/main"))),
            () => host.Title.Shown == "TuiCode/main — TuiCode",
            () =>
            {
                _git.OriginUrl = "https://github.com/mentaldesk/tui-style-guide.git";
                workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/code/tui-style-guide")));
            },
            () => host.Title.Shown == "tui-style-guide — TuiCode");

        Assert.Equal([Push, "TuiCode", "TuiCode/main — TuiCode", "tui-style-guide — TuiCode", Pop], Titles);
    }

    [Fact]
    public async Task A_lookup_that_finishes_after_the_folder_changed_again_leaves_the_newer_folder_s_name()
    {
        var older = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _git.OriginUrlLater[Full("/code/TuiCode/main")] = older;
        _git.OriginUrl = "https://github.com/mentaldesk/tui-style-guide.git";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var settled = System.Diagnostics.Stopwatch.StartNew();

        await HostSteps.Run(host,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/code/TuiCode/main"))),
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/code/tui-style-guide"))),
            () => host.Title.Shown == "tui-style-guide — TuiCode",
            () =>
            {
                older.SetResult("git@github.com:mentaldesk/TuiCode.git");
                settled.Restart();
            },
            () => settled.ElapsedMilliseconds > 500);

        Assert.Equal([Push, "TuiCode", "tui-style-guide — TuiCode", Pop], Titles);
    }

    [Fact]
    public async Task With_no_folder_the_tab_says_TuiCode()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, () => { });

        Assert.Equal([Push, "TuiCode", Pop], Titles);
    }

    [Fact]
    public async Task Leads_with_the_unseen_errors_until_the_status_bar_s_count_goes()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/code/tui-style-guide"))),
            () => host.Title.Shown == "tui-style-guide — TuiCode",
            () => { workbench.StatusBar.ShowNews("Broke", Severity.Error); },
            () => { workbench.StatusBar.ShowNews("Broke again", Severity.Error); },
            () => workbench.StatusBar.Bar.MarkRead());

        Assert.Equal(
            [Push, "TuiCode", "tui-style-guide — TuiCode", "⚠ 1 tui-style-guide — TuiCode", "⚠ 2 tui-style-guide — TuiCode", "tui-style-guide — TuiCode", Pop],
            Titles);
    }

    [Fact]
    public async Task The_command_palette_leaves_the_tab_as_it_was()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var workbenchWritesItsOwn = true;

        await HostSteps.Run(host,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/code/tui-style-guide"))),
            () => host.Title.Shown == "tui-style-guide — TuiCode",
            () => _commands.TryExecute(CommandIds.ShowActions),
            () => workbench.SubViews.OfType<ActionView>().Any(),
            () => { workbenchWritesItsOwn = workbench.Border.Settings.HasFlag(BorderSettings.TerminalTitle); },
            () => host.App.InjectKey(Key.Esc));

        Assert.False(workbenchWritesItsOwn);
        Assert.Equal([Push, "TuiCode", "tui-style-guide — TuiCode", Pop], Titles);
    }

    [Fact]
    public async Task Suspending_gives_the_terminal_its_own_title_and_fg_sets_TuiCode_s_again()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        host.ShellSuspend = new ShellSuspend(new FakeEnvironment(), () => true, _ => { }, () => _written.Add("stop"), host.Title);

        await HostSteps.Run(host,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/code/tui-style-guide"))),
            () => host.Title.Shown == "tui-style-guide — TuiCode",
            () => _commands.TryExecute(CommandIds.SuspendToShell));

        Assert.Equal(
            [Push, "TuiCode", "tui-style-guide — TuiCode", Pop, "stop", Push, "tui-style-guide — TuiCode", Pop],
            Titles);
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var host = new WorkbenchHost(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, fileSystem: _fs);
        host.WriteTitle = _written.Add;
        return host;
    }
}
