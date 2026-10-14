using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Drives `gm` / Ctrl+G M (#539) through the host. Boots a TG Application — serialised (#77).
public class MatchingBracketHostTests : StaticConfigurationTest
{
    private const int Closer = 41;

    private readonly MockFileSystem _fs = new();

    public MatchingBracketHostTests() =>
        _fs.AddFile("/work/a.cs", new MockFileData(string.Join('\n',
            ["{", .. Enumerable.Range(0, Closer - 1).Select(i => $"    Step({i});"), "}"])));

    [Fact]
    public async Task Ctrl_G_M_jumps_to_the_partner_and_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        (int, int) there = default;

        await HostSteps.Run(host,
            () => Open(workbench),
            () => Press(host),
            () => { there = Cursor(workbench); Press(host); });

        Assert.Equal((Closer, 0), there);
        Assert.Equal((0, 0), Cursor(workbench));
    }

    [Fact]
    public async Task Navigate_back_does_not_stop_at_a_bracket_jump()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => Open(workbench),
            () => commands.TryExecute(CommandIds.GoToMatchingBracket),
            () => host.App.InjectKey(Key.CursorUp),
            () => commands.TryExecute(CommandIds.NavigateBack));

        Assert.Equal((Closer - 1, 0), Cursor(workbench));
        Assert.Equal("No previous cursor position", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task Away_from_any_bracket_it_says_so()
    {
        _fs.AddFile("/work/a.cs", new MockFileData("int x;"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => Open(workbench),
            () => commands.TryExecute(CommandIds.GoToMatchingBracket));

        Assert.Equal("No matching bracket", workbench.StatusBar.Message);
    }

    [Fact]
    public void Gm_is_on_Ctrl_G_M_in_the_editor_with_a_mnemonic_and_a_place_in_the_Go_menu()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out var keybindings);

        Assert.Equal("Go to matching bracket", commands.Registered.Single(c => c.Id == CommandIds.GoToMatchingBracket).Label);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.GoToMatchingBracket));
        Assert.Equal("gm", CommandMnemonics.For(CommandIds.GoToMatchingBracket));
        Assert.Equal("Ctrl+G m", keybindings.Bindings.Single(b => b.CommandId == CommandIds.GoToMatchingBracket).Display);
        var go = host.Menu.Items.Select(i => i.Id).ToList();
        Assert.Equal(go.IndexOf(CommandIds.GoToSymbol) + 1, go.IndexOf(CommandIds.GoToMatchingBracket));
    }

    [Fact]
    public async Task A_key_bound_in_settings_jumps()
    {
        var settings = new InMemorySettingsService();
        settings.SetKeybindingOverrides([new KeybindingOverride(TestKeys.Chord("F6"), CommandIds.GoToMatchingBracket)]);
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), settings,
            driverName: DriverRegistry.Names.ANSI);

        await HostSteps.Run(host,
            () => Open(workbench),
            () => host.App.InjectKey(Key.F6),
            () => Cursor(workbench) == (Closer, 0));
    }

    private static void Press(WorkbenchHost host)
    {
        host.App.InjectKey(Key.G.WithCtrl);
        host.App.InjectKey(Key.M);
    }

    private void Open(Workbench.Workbench workbench)
    {
        workbench.OpenFile(_fs.FileInfo.New("/work/a.cs"));
        workbench.Editor.Group.ActiveTab!.FocusContent();
    }

    private static (int, int) Cursor(Workbench.Workbench workbench) =>
        (workbench.Editor.Group.ActiveTab!.CursorRow, workbench.Editor.Group.ActiveTab.CursorColumn);

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands) =>
        BuildHost(workbench, out commands, out _);

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands, out KeybindingService keybindings)
    {
        commands = new CommandService();
        keybindings = new KeybindingService(commands);
        return new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
