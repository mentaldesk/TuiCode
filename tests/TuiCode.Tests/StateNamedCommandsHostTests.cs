using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// The sidebar, gutter, word wrap and column select are named for what choosing them will do now (#526). Boots a TG
// Application — serialised (#77).
public class StateNamedCommandsHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;

    public StateNamedCommandsHostTests()
    {
        _keybindings = new KeybindingService(_commands);
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\n"));
    }

    public static TheoryData<string, string, string> Toggles => new()
    {
        { CommandIds.ToggleSidebar, "Hide sidebar", "Show sidebar" },
        { CommandIds.ToggleGutter, "Hide gutter", "Show gutter" },
        { CommandIds.ToggleWordWrap, "Turn off word wrap", "Turn on word wrap" },
        { CommandIds.ToggleColumnSelect, "Turn off column select", "Turn on column select" },
    };

    [Theory]
    [MemberData(nameof(Toggles))]
    public async Task Each_is_titled_for_what_it_will_do_and_the_title_follows_the_state(string id, string whileOn, string whileOff)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        bool before = false, after = true;
        string? first = null;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { before = IsOn(workbench, id); first = Title(id); return _commands.TryExecute(id); },
            () => (after = IsOn(workbench, id)) != before);

        Assert.Equal(before ? whileOn : whileOff, first);
        Assert.Equal(after ? whileOn : whileOff, Title(id));
    }

    [Fact]
    public async Task The_menu_letters_stay_put_as_the_titles_change()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string[] ids = [CommandIds.ToggleSidebar, CommandIds.ToggleGutter, CommandIds.ToggleWordWrap, CommandIds.ToggleColumnSelect];
        (string Title, Key HotKey)[] before = [], after = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { host.Menu.ShowAvailable(); before = [.. ids.Select(id => Shown(host, id))]; return true; },
            () => ids.All(_commands.TryExecute),
            () => { host.Menu.ShowAvailable(); after = [.. ids.Select(id => Shown(host, id))]; return true; });

        Assert.All(before.Zip(after), pair => Assert.NotEqual(pair.First.Title, pair.Second.Title));
        Assert.Equal(before.Select(b => b.HotKey), after.Select(a => a.HotKey));
        Assert.Equal(["Hide _sidebar", "Hide _gutter"], before.Take(2).Select(b => b.Title));
        Assert.Equal(["_Show sidebar", "Show _gutter", "_Turn off column select"],
            after.Where((_, i) => i != 2).Select(a => a.Title));
    }

    [Theory]
    [InlineData("hide", "Hide sidebar", "Hide gutter")]
    [InlineData("show", "Hide sidebar", "Hide gutter")]
    [InlineData("turn", "Turn on column select")]
    [InlineData("wrap", "Turn off word wrap")]
    [InlineData("toggle", "Hide sidebar", "Hide gutter", "Turn off word wrap", "Turn on column select", "Toggle line comment")]
    public async Task The_palette_finds_them_under_their_current_name(string query, params string[] expected)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> found = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { workbench.Editor.Group.ActiveTab!.WordWrap = true; return true; },
            () => { found = Palette(query); return true; });

        Assert.All(expected, title => Assert.Contains(title, found));
        Assert.DoesNotContain(found, label => label.StartsWith("Toggle", StringComparison.Ordinal) && label != "Toggle line comment");
    }

    [Fact]
    public void The_mnemonic_list_and_Settings_name_them_both_ways()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string[] names = ["Show or hide sidebar", "Show or hide gutter", "Word wrap on or off", "Column select on or off", "Mark viewed or not"];

        var mnemonics = WorkbenchHost.MnemonicsInScope(_commands, CommandScope.Editor).Select(e => e.Label).ToList();
        var settings = KeybindingRows.Build(_commands.Registered, [.. _keybindings.Bindings], "").Select(r => r.Label).ToList();

        Assert.Contains("Show or hide sidebar", mnemonics);
        Assert.Contains("Column select on or off", mnemonics);
        Assert.All(names, name => Assert.Contains(name, settings));
    }

    private string Title(string id) => _commands.Registered.Single(c => c.Id == id).Title;

    private static bool IsOn(Workbench.Workbench workbench, string id) => id switch
    {
        CommandIds.ToggleSidebar => workbench.IsSidebarVisible,
        CommandIds.ToggleGutter => workbench.Editor.Groups.GutterVisible,
        CommandIds.ToggleWordWrap => workbench.Editor.Group.ActiveTab!.WordWrap,
        _ => workbench.Editor.Groups.ColumnSelect,
    };

    private static (string Title, Key HotKey) Shown(WorkbenchHost host, string id) =>
        host.Menu.Items.Single(i => i.Id == id).Item is var item ? (item.Title, item.HotKey) : default;

    private IReadOnlyList<string> Palette(string query)
    {
        using var view = new ActionView(_commands, new KeybindingService(_commands), CommandScope.Editor, _ => { });
        view.SubViews.OfType<TextField>().Single().Text = query;
        return view.Labels;
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, _keybindings, new InputScopeStack(), new InMemorySettingsService(),
            driverName: DriverRegistry.Names.ANSI);
}
