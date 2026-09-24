using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Settings → Interface (#209). Boots a TG Application — serialised (#77).
public class InterfaceSettingsViewTests : StaticConfigurationTest
{
    private readonly InMemorySettingsService _settings = new();

    [Fact]
    public void The_panel_reports_the_width_it_was_given()
    {
        using var view = new InterfaceSettingsView(45);

        Assert.Equal(45, view.Current);
    }

    [Fact]
    public async Task A_typed_width_is_saved_and_applied()
    {
        _settings.SidebarWidth = 40;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host), OpenInterfaceSettings(host, workbench),
            () => host.App.InjectKey(Key.CursorUp),
            () => host.App.InjectKey(Key.Enter.WithCtrl));

        Assert.Equal(41, _settings.SidebarWidth);
        Assert.Equal(41, workbench.DrawnSidebarWidth);
        Assert.Equal(1, _settings.SaveCount);
    }

    [Fact]
    public async Task Cancelling_leaves_the_width_as_it_was()
    {
        _settings.SidebarWidth = 40;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host), OpenInterfaceSettings(host, workbench),
            () => host.App.InjectKey(Key.CursorUp),
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(40, _settings.SidebarWidth);
        Assert.Equal(40, workbench.DrawnSidebarWidth);
    }

    [Theory]
    [InlineData(SidebarSizing.SettingsMax, "CursorUp")]
    [InlineData(SidebarSizing.Min, "CursorDown")]
    public async Task The_spinner_stays_within_its_limits(int width, string key)
    {
        _settings.SidebarWidth = width;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, Size(host), OpenInterfaceSettings(host, workbench),
            () => host.App.InjectKey(TestKeys.Chord(key)[0]),
            () => host.App.InjectKey(Key.Enter.WithCtrl));

        Assert.Equal(width, _settings.SidebarWidth);
    }

    [Fact]
    public async Task Left_returns_to_the_categories()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var panelFocused = true;

        await HostSteps.Run(host, Size(host), OpenInterfaceSettings(host, workbench),
            () => host.App.InjectKey(Key.CursorLeft),
            () => { panelFocused = Panel(workbench).SubViews.Any(v => v.HasFocus); },
            () => host.App.InjectKey(Key.Esc));

        Assert.False(panelFocused);
    }

    // Interface is the last category, so End lands on it whatever else is in the list.
    private static Func<bool> OpenInterfaceSettings(WorkbenchHost host, Workbench.Workbench workbench)
    {
        var step = 0;
        return () =>
        {
            switch (step++)
            {
                case 0: host.App.InjectKey(TestKeys.Chord("Ctrl+,")[0]); return false;
                case 1: host.App.InjectKey(Key.End); return false;
                case 2: host.App.InjectKey(Key.CursorRight); return false;
                default:
                    return Panel(workbench) is { Visible: true } panel && panel.SubViews.Any(v => v.HasFocus);
            }
        };
    }

    private static InterfaceSettingsView Panel(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<SettingsView>().Single().SubViews.OfType<InterfaceSettingsView>().Single();

    private static Action Size(WorkbenchHost host) => () => host.App.Driver!.SetScreenSize(120, 24);

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI);
    }

    private static Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
}
