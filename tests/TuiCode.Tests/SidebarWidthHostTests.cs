using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Widen / narrow sidebar (#209). Boots a TG Application — serialised (#77).
public class SidebarWidthHostTests : StaticConfigurationTest
{
    private const int WideTerminal = 120;
    private readonly InMemorySettingsService _settings = new();

    [Fact]
    public async Task Widen_and_narrow_move_the_sidebar_by_a_step()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var widths = new List<int>();

        await HostSteps.Run(host, Size(host, WideTerminal),
            () => { commands.TryExecute(CommandIds.WidenSidebar); },
            () => { widths.Add(workbench.DrawnSidebarWidth); },
            () => { commands.TryExecute(CommandIds.NarrowSidebar); },
            () => { commands.TryExecute(CommandIds.NarrowSidebar); },
            () => { widths.Add(workbench.DrawnSidebarWidth); });

        Assert.Equal([SidebarSizing.Default + 5, SidebarSizing.Default - 5], widths);
    }

    [Fact]
    public async Task Widening_stops_where_the_editor_would_lose_its_floor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var message = "";

        await HostSteps.Run(host, Size(host, WideTerminal),
            () => { for (var i = 0; i < 20; i++) commands.TryExecute(CommandIds.WidenSidebar); },
            () => { message = workbench.StatusBar.DisplayedText; });

        Assert.Equal(WideTerminal - SidebarSizing.EditorFloor, workbench.DrawnSidebarWidth);
        Assert.Equal($"Sidebar width: 80 (maximum — the editor needs 40 columns)", message);
    }

    [Fact]
    public async Task Narrowing_stops_at_the_sidebar_floor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var message = "";

        await HostSteps.Run(host, Size(host, WideTerminal),
            () => { for (var i = 0; i < 20; i++) commands.TryExecute(CommandIds.NarrowSidebar); },
            () => { message = workbench.StatusBar.DisplayedText; });

        Assert.Equal(SidebarSizing.Min, workbench.DrawnSidebarWidth);
        Assert.Equal("Sidebar width: 15 (minimum)", message);
    }

    [Fact]
    public async Task A_width_away_from_both_limits_is_reported_without_a_suffix()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var message = "";

        await HostSteps.Run(host, Size(host, WideTerminal),
            () => { commands.TryExecute(CommandIds.WidenSidebar); },
            () => { message = workbench.StatusBar.DisplayedText; });

        Assert.Equal("Sidebar width: 35", message);
    }

    [Theory]
    [InlineData(CommandIds.WidenSidebar)]
    [InlineData(CommandIds.NarrowSidebar)]
    public async Task With_the_sidebar_hidden_either_command_says_how_to_show_it(string commandId)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var message = "";

        await HostSteps.Run(host, Size(host, WideTerminal),
            () => { commands.TryExecute(CommandIds.ToggleSidebar); },
            () => { commands.TryExecute(commandId); },
            () => { message = workbench.StatusBar.DisplayedText; });

        Assert.Equal("Sidebar is hidden — use ts to show it", message);
        Assert.Equal(SidebarSizing.Default, workbench.DrawnSidebarWidth);
        Assert.Equal(SidebarSizing.Default, _settings.SidebarWidth);
        Assert.Equal(0, _settings.SaveCount);
    }

    [Fact]
    public async Task A_narrower_terminal_re_clamps_the_drawn_width_and_a_wider_one_restores_the_choice()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var squeezed = 0;

        await HostSteps.Run(host, Size(host, WideTerminal),
            () => { for (var i = 0; i < 10; i++) commands.TryExecute(CommandIds.WidenSidebar); },
            Size(host, 80),
            () => { squeezed = workbench.DrawnSidebarWidth; },
            Size(host, WideTerminal));

        Assert.Equal(80 - SidebarSizing.EditorFloor, squeezed);
        Assert.Equal(WideTerminal - SidebarSizing.EditorFloor, workbench.SidebarWidth);
        Assert.Equal(WideTerminal - SidebarSizing.EditorFloor, workbench.DrawnSidebarWidth);
    }

    [Fact]
    public async Task A_new_width_is_saved()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, Size(host, WideTerminal),
            () => { commands.TryExecute(CommandIds.WidenSidebar); });

        Assert.Equal(SidebarSizing.Default + 5, _settings.SidebarWidth);
        Assert.Equal(1, _settings.SaveCount);
    }

    [Fact]
    public async Task A_saved_width_applies_at_startup()
    {
        _settings.SidebarWidth = 45;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host, Size(host, WideTerminal));

        Assert.Equal(45, workbench.DrawnSidebarWidth);
    }

    [Fact]
    public async Task The_mnemonic_overlay_resolves_ws_and_ns()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var widened = 0;

        await HostSteps.Run(host, Size(host, WideTerminal),
            Leader(host),
            () => host.App.InjectKey(Key.W),
            () => host.App.InjectKey(Key.S),
            () => { widened = workbench.DrawnSidebarWidth; },
            Leader(host),
            () => host.App.InjectKey(Key.N),
            () => host.App.InjectKey(Key.S));

        Assert.Equal(SidebarSizing.Default + 5, widened);
        Assert.Equal(SidebarSizing.Default, workbench.DrawnSidebarWidth);
    }

    [Fact]
    public void Neither_command_is_bound_by_default()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI);

        Assert.DoesNotContain(keybindings.Bindings,
            b => b.CommandId is CommandIds.WidenSidebar or CommandIds.NarrowSidebar);
        var rows = KeybindingRows.Build(commands.Registered, keybindings.Bindings.ToArray(), "sidebar")
            .Where(r => r.CommandId is CommandIds.WidenSidebar or CommandIds.NarrowSidebar)
            .ToArray();
        Assert.Equal([KeybindingRows.Unbound, KeybindingRows.Unbound], rows.Select(r => r.Keys));
        // Global, so they fire wherever focus is — the editor, the tree, the Find results, the Review tree.
        Assert.All(rows, r => Assert.Equal(CommandScope.Global, r.Scope));
    }

    private static Action Leader(WorkbenchHost host) =>
        () => { if (Key.TryParse("Ctrl+Space", out var leader)) host.App.InjectKey(leader); };

    // Resizing the terminal takes effect on the next layout, so it gets its own step.
    private static Action Size(WorkbenchHost host, int width) =>
        () => host.App.Driver!.SetScreenSize(width, 24);

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI);
    }

    private static Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
}
