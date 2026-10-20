using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// #562. Boots a TG Application — serialised (#77).
public class WatchTutorialsHostTests : StaticConfigurationTest
{
    private const string Url = "https://www.youtube.com/playlist?list=PLGokw8JfNpWE";

    private readonly FakeBrowser _browser = new();
    private readonly FakeEnvironment _environment = new();

    [Fact]
    public void It_is_a_global_command_with_mnemonic_wt_and_no_default_key()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out var keybindings);

        Assert.Equal(CommandScope.Global, commands.ScopeOf(CommandIds.WatchTutorials));
        Assert.True(commands.IsEnabled(CommandIds.WatchTutorials));
        Assert.Equal("wt", CommandMnemonics.For(CommandIds.WatchTutorials));
        Assert.DoesNotContain(keybindings.Bindings, b => b.CommandId == CommandIds.WatchTutorials);
        Assert.Contains(KeybindingRows.Build(commands.Registered, [.. keybindings.Bindings], ""),
            r => r.CommandId == CommandIds.WatchTutorials && r.Label == "Watch tutorials" && r.Binding is null);
    }

    [Fact]
    public void It_heads_the_help_menus_second_group_with_letter_t()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _, out _);
        var help = workbench.MenuBar.SubViews.OfType<MenuBarItem>().Single(m => m.Title == "_Help")
            .PopoverMenu!.Root!.SubViews.OfType<CommandMenuItem>();

        Assert.Equal(
        [
            "Show _keys", "_Show all commands", "Show _mnemonics",
            "Watch _tutorials", "Show _diagnostics", "Show _log", "_About TuiCode",
        ], help.Select(i => i.Title));
    }

    [Fact]
    public async Task With_nothing_open_the_palette_lists_it_and_the_leader_runs_it_with_wt()
    {
        using var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        using var host = BuildHost(workbench, out _, out _);
        IReadOnlyList<string> labels = [];

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { labels = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.W),
            () => host.App.InjectKey(Key.T),
            () => _browser.Opened.Count == 1);

        Assert.Contains("Watch tutorials", labels);
        Assert.Equal([Url], _browser.Opened);
    }

    [Fact]
    public async Task Locally_it_opens_the_playlist_and_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out _);

        await HostSteps.Run(host, () => commands.TryExecute(CommandIds.WatchTutorials));

        Assert.Equal([Url], _browser.Opened);
        Assert.Equal("Opened TuiCode tutorials in your browser", workbench.StatusBar.Message);
        Assert.False(workbench.StatusBar.ShowsError);
    }

    [Fact]
    public async Task A_browser_that_cannot_be_launched_says_why()
    {
        _browser.Failure = "xdg-open not found";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out _);

        await HostSteps.Run(host, () => commands.TryExecute(CommandIds.WatchTutorials));

        Assert.Equal("Couldn't open the link: xdg-open not found", workbench.StatusBar.Message);
        Assert.True(workbench.StatusBar.ShowsError);
    }

    [Fact]
    public async Task Over_ssh_the_link_is_copied_instead()
    {
        _environment.Set("SSH_CONNECTION", "10.0.0.1 50000 10.0.0.2 22");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out _);
        var clipboard = new TestClipboard();

        await HostSteps.Run(host,
            () => { host.App.Driver!.Clipboard = clipboard; },
            () => commands.TryExecute(CommandIds.WatchTutorials));

        Assert.Empty(_browser.Opened);
        Assert.Equal(Url, clipboard.Text);
        Assert.Equal("Copied the tutorials link (no browser over SSH)", workbench.StatusBar.Message);
        Assert.False(workbench.StatusBar.ShowsError);
    }

    [Fact]
    public async Task Over_ssh_a_clipboard_that_fails_says_why()
    {
        _environment.Set("SSH_CONNECTION", "10.0.0.1 50000 10.0.0.2 22");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out _);
        host.CopyToClipboard = _ => new CopyOutcome.Failed("no clipboard tool found (install xclip)");

        await HostSteps.Run(host, () => commands.TryExecute(CommandIds.WatchTutorials));

        Assert.Empty(_browser.Opened);
        Assert.Equal("Couldn't copy the link: no clipboard tool found (install xclip)", workbench.StatusBar.Message);
        Assert.True(workbench.StatusBar.ShowsError);
    }

    private static ActionView? Palette(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ActionView>().SingleOrDefault();

    private static Workbench.Workbench BuildWorkbench()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands, out KeybindingService keybindings)
    {
        commands = new CommandService();
        keybindings = new KeybindingService(commands);
        return new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), environment: _environment, driverName: DriverRegistry.Names.ANSI) { Browser = _browser };
    }

    private sealed class FakeBrowser : IBrowser
    {
        public List<string> Opened { get; } = [];
        public string? Failure { get; set; }

        public string? Open(string url)
        {
            if (Failure is null) Opened.Add(url);
            return Failure;
        }
    }
}
