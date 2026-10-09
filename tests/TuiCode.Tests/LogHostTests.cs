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
    public async Task Show_log_opens_the_log_and_clears_the_count_until_the_next_warning()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        List<string> shown = [];

        await HostSteps.Run(host,
            () => _logger.LogWarning("one"),
            () => _logger.LogWarning("two"),
            () => shown.Add(workbench.StatusBar.DisplayedWarnings),
            () => _commands.TryExecute(CommandIds.ShowLog),
            () => shown.Add(workbench.StatusBar.DisplayedWarnings),
            () => _logger.LogWarning("three"),
            () => shown.Add(workbench.StatusBar.DisplayedWarnings));

        Assert.Equal(["⚠ 2", "", "⚠ 1"], shown);
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
    public async Task Clicking_the_count_opens_the_log_and_clears_it()
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
        Assert.Equal(_log.File.FullName, workbench.Editor.Group.ActiveTab?.File.FullName);
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
