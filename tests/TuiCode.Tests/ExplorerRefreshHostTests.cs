using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Refresh explorer from the palette, the leader and a user's own key (#332). Boots a TG Application — serialised (#77).
public class ExplorerRefreshHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;

    public ExplorerRefreshHostTests() => _keybindings = new KeybindingService(_commands);

    [Fact]
    public void Is_a_global_command_with_a_label_mnemonic_and_no_default_key()
    {
        using var workbench = BuildWorkbench();
        using var _ = BuildHost(workbench);

        var command = Assert.Single(_commands.Registered, c => c.Id == CommandIds.RefreshExplorer);
        Assert.Equal("Refresh explorer", command.Label);
        Assert.Equal(CommandScope.Global, command.Scope);
        Assert.Equal("re", CommandMnemonics.For(CommandIds.RefreshExplorer));
        Assert.DoesNotContain(_keybindings.Bindings, b => b.CommandId == CommandIds.RefreshExplorer);
    }

    [Fact]
    public async Task The_palette_refreshes_the_explorer()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            ChangeOnDisk,
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { foreach (var c in "refresh explorer") host.App.InjectKey(new Key(c)); },
            () => Palette(workbench)!.Labels.SequenceEqual(["Refresh explorer"]),
            () => host.App.InjectKey(Key.Enter),
            () => Palette(workbench) is null);

        Assert.Equal(["b.txt"], Names(workbench));
    }

    [Fact]
    public async Task The_leader_then_re_refreshes_the_explorer()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            ChangeOnDisk,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.R),
            () => host.App.InjectKey(Key.E),
            () => Names(workbench).SequenceEqual(["b.txt"]));
    }

    [Fact]
    public async Task A_key_bound_in_settings_refreshes_the_explorer()
    {
        using var workbench = BuildWorkbench();
        var settings = new InMemorySettingsService();
        settings.SetKeybindingOverrides([new KeybindingOverride(TestKeys.Chord("F5"), CommandIds.RefreshExplorer)]);
        using var host = BuildHost(workbench, settings);

        await HostSteps.Run(host,
            ChangeOnDisk,
            () => host.App.InjectKey(Key.F5),
            () => Names(workbench).SequenceEqual(["b.txt"]));
    }

    [Fact]
    public async Task Refreshing_from_the_explorers_leader_leaves_the_keys_in_the_explorer()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => _commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            ChangeOnDisk,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.R),
            () => host.App.InjectKey(Key.E),
            () => Names(workbench).SequenceEqual(["b.txt"]));

        Assert.Equal("Explorer", workbench.StatusBar.DisplayedFocus);
    }

    private void ChangeOnDisk()
    {
        _fs.File.Delete("/work/a.txt");
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
    }

    private static string[] Names(Workbench.Workbench workbench) =>
        workbench.Sidebar.Explorer.GetChildren(workbench.Sidebar.Explorer.Root!).Select(c => c.Name).ToArray();

    private static ActionView? Palette(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ActionView>().SingleOrDefault();

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, ISettingsService? settings = null) =>
        new(workbench, _commands, _keybindings, new InputScopeStack(), settings ?? new InMemorySettingsService(),
            driverName: DriverRegistry.Names.ANSI);
}
