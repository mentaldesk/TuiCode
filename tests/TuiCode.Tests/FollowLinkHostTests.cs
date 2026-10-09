using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// #466. Boots a TG Application — serialised (#77).
public class FollowLinkHostTests : StaticConfigurationTest
{
    private const string Url = "https://example.com/a";

    private readonly MockFileSystem _fs = new();
    private readonly FakeBrowser _browser = new();
    private readonly FakeEnvironment _environment = new();

    public FollowLinkHostTests() =>
        _fs.AddFile("/work/a.md", new MockFileData($"no link here\nSee {Url}.\n"));

    [Fact]
    public async Task Following_the_link_under_the_cursor_opens_it_and_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => Open(workbench, row: 1, column: 8),
            () => commands.TryExecute(CommandIds.FollowLink));

        Assert.Equal([Url], _browser.Opened);
        Assert.Equal($"Opened {Url}", workbench.StatusBar.Message);
        Assert.False(workbench.StatusBar.ShowsError);
    }

    [Fact]
    public async Task A_browser_that_cannot_be_launched_says_why()
    {
        _browser.Failure = "xdg-open not found";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => Open(workbench, row: 1, column: 8),
            () => commands.TryExecute(CommandIds.FollowLink));

        Assert.Equal("Couldn't open the link: xdg-open not found", workbench.StatusBar.Message);
        Assert.True(workbench.StatusBar.ShowsError);
    }

    [Fact]
    public async Task Over_ssh_the_link_is_copied_instead_and_pastes_into_the_editor()
    {
        _environment.Set("SSH_CONNECTION", "10.0.0.1 50000 10.0.0.2 22");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var clipboard = new TestClipboard();
        var said = "";

        await HostSteps.Run(host,
            () =>
            {
                host.App.Driver!.Clipboard = clipboard;
                Open(workbench, row: 1, column: 8);
            },
            () =>
            {
                commands.TryExecute(CommandIds.FollowLink);
                said = workbench.StatusBar.Message;
                workbench.Editor.Group.ActiveTab!.MoveCursor(0, 0);
            },
            () => host.App.InjectKey(Key.V.WithCtrl),
            () => workbench.Editor.Group.ActiveTab!.Lines[0].StartsWith(Url, StringComparison.Ordinal));

        Assert.Empty(_browser.Opened);
        Assert.Equal(Url, clipboard.Text);
        Assert.Equal($"Copied {Url} (no browser over SSH)", said);
    }

    [Fact]
    public async Task Off_a_link_the_command_is_disabled_and_left_out_of_the_palette_and_the_leader()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        bool offLink = true, onLink = false;
        IReadOnlyList<string> offLabels = [], onLabels = [];

        await HostSteps.Run(host,
            () => Open(workbench, row: 0, column: 3),
            () =>
            {
                offLink = commands.IsEnabled(CommandIds.FollowLink);
                Assert.DoesNotContain(Mnemonics(commands, CommandScope.Editor), m => m == "fl");
            },
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { offLabels = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null,
            () => workbench.Editor.Group.ActiveTab!.MoveCursor(1, 4),
            () =>
            {
                onLink = commands.IsEnabled(CommandIds.FollowLink);
                Assert.Contains(Mnemonics(commands, CommandScope.Editor), m => m == "fl");
                Assert.DoesNotContain(Mnemonics(commands, CommandScope.Explorer), m => m == "fl");
            },
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { onLabels = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null);

        Assert.False(offLink);
        Assert.True(onLink);
        Assert.DoesNotContain("Follow link", offLabels);
        Assert.Contains("Follow link", onLabels);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.FollowLink));
    }

    [Fact]
    public async Task The_leader_follows_the_link_with_fl()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host,
            () => Open(workbench, row: 1, column: 8),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.F),
            () => host.App.InjectKey(Key.L),
            () => _browser.Opened.Count == 1);

        Assert.Equal([Url], _browser.Opened);
    }

    [Fact]
    public void Keyboard_Shortcuts_lists_it_with_no_default_key()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        var rows = KeybindingRows.Build(commands.Registered, [.. keybindings.Bindings], "");

        Assert.Contains(rows, r => r.CommandId == CommandIds.FollowLink && r.Label == "Follow link" && r.Binding is null);
    }

    [Fact]
    public async Task A_key_bound_in_settings_follows_the_link()
    {
        var settings = new InMemorySettingsService();
        settings.SetKeybindingOverrides([new KeybindingOverride(TestKeys.Chord("F6"), CommandIds.FollowLink)]);
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), settings,
            environment: _environment, driverName: DriverRegistry.Names.ANSI) { Browser = _browser };

        await HostSteps.Run(host,
            () => Open(workbench, row: 1, column: 8),
            () => host.App.InjectKey(Key.F6),
            () => _browser.Opened.Count == 1);

        Assert.Equal([Url], _browser.Opened);
    }

    private void Open(Workbench.Workbench workbench, int row, int column)
    {
        workbench.OpenFile(_fs.FileInfo.New("/work/a.md"));
        workbench.Editor.Group.ActiveTab!.MoveCursor(row, column);
    }

    private static IEnumerable<string> Mnemonics(CommandService commands, CommandScope scope) =>
        WorkbenchHost.MnemonicsInScope(commands, scope).Select(m => m.Mnemonic);

    private static ActionView? Palette(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ActionView>().SingleOrDefault();

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
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

public class SystemBrowserTests
{
    [Fact]
    public void A_missing_opener_says_it_was_not_found() =>
        Assert.Equal("tuicode-no-such-opener not found", new SystemBrowser("tuicode-no-such-opener").Open("https://example.com"));
}
