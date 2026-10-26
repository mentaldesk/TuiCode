using MentalDesk.Tui.Chrome;
using Microsoft.Extensions.Logging;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Logging;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// The log, Show log and the status bar's ⚠ count (#473). Boots a TG Application — serialised (#77).
public class LogHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly LogFile _log;
    private readonly ILogger _logger;
    private readonly CommandService _commands = new();
    private readonly InMemorySettingsService _settings = new();

    public LogHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _log = new LogFile(_fs, "/home/.tui/logs");
        _logger = _log.CreateLogger("test");
    }

    [Fact]
    public async Task Log_warnings_count_in_the_bar_and_list_in_messages_without_showing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        List<string> shown = [];
        var message = "";
        IReadOnlyList<string> listed = [];

        await HostSteps.Run(host,
            () => _logger.LogWarning("one"),
            () => _logger.LogWarning("two"),
            () =>
            {
                shown.Add(workbench.StatusBar.DisplayedWarnings);
                message = workbench.StatusBar.DisplayedText;
            },
            () => _commands.TryExecute(CommandIds.ShowLog),
            () => shown.Add(workbench.StatusBar.DisplayedWarnings),
            () => _commands.TryExecute(CommandIds.ShowMessages),
            () =>
            {
                shown.Add(workbench.StatusBar.DisplayedWarnings);
                listed = [.. Messages(workbench).Rows.Select(row => row.Text)];
            },
            () => host.App.InjectKey(Key.Esc),
            () => _logger.LogWarning("three"),
            () => shown.Add(workbench.StatusBar.DisplayedWarnings));

        Assert.Equal(["⚠ 2", "⚠ 2", "", "⚠ 1"], shown);
        Assert.Equal(workbench.StatusBar.DefaultMessage, message);
        Assert.Equal(["two", "one"], listed);
    }

    [Fact]
    public async Task Show_log_opens_the_log()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => _logger.LogWarning("two"),
            () => _commands.TryExecute(CommandIds.ShowLog));

        Assert.Equal(_log.File.FullName, workbench.Editor.Group.ActiveTab?.File.FullName);
        Assert.Contains("WARN  two", workbench.Editor.Group.ActiveTab!.Content);
    }

    [Fact]
    public async Task Show_log_switches_to_the_log_when_it_is_already_open()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => _commands.TryExecute(CommandIds.ShowLog),
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => _commands.TryExecute(CommandIds.ShowLog));

        Assert.Equal(2, workbench.Editor.Group.Tabs.Count());
        Assert.Equal(_log.File.FullName, workbench.Editor.Group.ActiveTab?.File.FullName);
    }

    [Fact]
    public async Task Clicking_the_count_opens_messages_and_clears_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var shown = "";

        await HostSteps.Run(host,
            () => _logger.LogWarning("one"),
            () => workbench.StatusBar.DisplayedWarnings == "⚠ 1",
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () =>
            {
                var at = workbench.StatusBar.WarningsView.FrameToScreen().Location;
                host.App.InjectMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, ScreenPosition = at });
                host.App.InjectMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, ScreenPosition = at });
            },
            () => { shown = workbench.StatusBar.DisplayedWarnings; });

        Assert.Equal("", shown);
        Assert.NotNull(Messages(workbench));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Startup_says_so_only_when_the_settings_file_did_not_parse(bool invalid)
    {
        _settings.SettingsFileInvalid = invalid;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        host.OpenWhenRunning(new StartupTarget(_fs.DirectoryInfo.New("/work"), []));
        var message = "";

        await HostSteps.Run(host,
            () => workbench.Sidebar.Explorer.Root is not null,
            () => { message = workbench.StatusBar.DisplayedText; });

        Assert.Equal(invalid, message == WorkbenchHost.SettingsErrorMessage);
    }

    [Fact]
    public void Show_log_sits_below_show_diagnostics_in_the_help_menu()
    {
        var help = CommandMenu.Layout.Single(menu => menu.Title == "_Help").Ids;

        Assert.Equal(Array.IndexOf(help, CommandIds.ShowDiagnostics) + 1, Array.IndexOf(help, CommandIds.ShowLog));
    }

    [Fact]
    public void Show_messages_sits_below_show_log_in_the_help_menu()
    {
        var help = CommandMenu.Layout.Single(menu => menu.Title == "_Help").Ids;

        Assert.Equal(Array.IndexOf(help, CommandIds.ShowLog) + 1, Array.IndexOf(help, CommandIds.ShowMessages));
    }

    [Fact]
    public void Show_messages_is_sm()
    {
        Assert.Equal("sm", CommandMnemonics.For(CommandIds.ShowMessages));
    }

    private static MessagesDialog Messages(Workbench.Workbench workbench) => workbench.SubViews.OfType<MessagesDialog>().Single();

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI, fileSystem: _fs, log: _log);
}
