using System.Drawing;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Themes;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Tests;

// The menu bar (#340). Boots a TG Application — serialised (#77).
public class MenuBarHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();

    public MenuBarHostTests()
    {
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\nthree\n"));
    }

    [Fact]
    public void Every_command_with_a_mnemonic_has_exactly_one_menu_item()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var mapped = MenuIds().ToList();

        Assert.All(CommandMnemonics.All, pair => Assert.Single(mapped, id => id == pair.Key));
        Assert.Contains(CommandIds.ShowActions, mapped);
        Assert.Contains(CommandIds.ShowMnemonics, mapped);
        Assert.Equal(mapped.Count, mapped.Distinct().Count());
        Assert.All(mapped, id => Assert.True(commands.IsRegistered(id), $"{id} isn't a registered command"));
    }

    [Fact]
    public void The_menus_read_across_the_top_with_distinct_hot_letters()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var menus = workbench.MenuBar.SubViews.OfType<MenuBarItem>().ToList();

        Assert.Equal(["File", "Edit", "Selection", "View", "Go", "Diff", "Review", "Help"],
            menus.Select(m => m.Title.Replace("_", "")));
        Assert.Equal(menus.Count, menus.Select(m => m.HotKey).Distinct().Count());
        Assert.All(menus, m => Assert.Equal(m.Title[1], (char)m.HotKey.KeyCode));
    }

    [Fact]
    public async Task The_bar_sits_above_the_sidebar_and_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var (bar, sidebar, editor, width) = (default(Rectangle), default(Rectangle), default(Rectangle), 0);

        await HostSteps.Run(host,
            () => workbench.MenuBar.Frame.Width > 0,
            () => { (bar, sidebar, editor, width) = (workbench.MenuBar.FrameToScreen(), workbench.Sidebar.FrameToScreen(),
                workbench.Editor.FrameToScreen(), workbench.FrameToScreen().Width); });

        Assert.Equal(new Rectangle(0, 0, width, 1), bar);
        Assert.Equal(1, sidebar.Y);
        Assert.Equal(1, editor.Y);
        Assert.Equal(workbench.StatusBar.FrameToScreen().Y, sidebar.Bottom);
    }

    [Fact]
    public async Task F10_opens_File_and_Esc_hands_the_keys_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        string? first = null;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.F10),
            () => workbench.MenuBar.IsOpen(),
            () => { first = Focused(workbench); host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen() && workbench.Editor.Group.ActiveTab!.HasFocus);

        Assert.Equal("New file or folder", first);
        Assert.Equal("Editor", workbench.StatusBar.DisplayedFocus);
    }

    [Fact]
    public async Task Show_menu_can_be_rebound()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host,
            () => host.ApplyKeybindings(
            [
                new KeybindingOverride(TestKeys.Chord("F10"), "-" + CommandIds.ShowMenu),
                new KeybindingOverride(TestKeys.Chord("F9"), CommandIds.ShowMenu),
            ]),
            () => host.App.InjectKey(Key.F10),
            () => Assert.False(workbench.MenuBar.IsOpen()),
            () => host.App.InjectKey(Key.F9),
            () => workbench.MenuBar.IsOpen(),
            () => host.App.InjectKey(Key.Esc),
            () => !workbench.MenuBar.IsOpen());
    }

    // Arrows walk the menu, and Enter runs the item once: a second binding would move the line twice.
    [Fact]
    public async Task Edit_Move_line_down_moves_the_editors_line_once()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.F10),
            () => workbench.MenuBar.IsOpen(),
            () => host.App.InjectKey(Key.CursorRight),
            () => host.App.InjectKey(Key.CursorDown),
            () => Focused(workbench) == "Move line down",
            () => host.App.InjectKey(Key.Enter),
            () => !workbench.MenuBar.IsOpen() && workbench.Editor.Group.ActiveTab!.Content != "one\ntwo\nthree\n");

        Assert.Equal("two\none\nthree\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
        Assert.Equal("Editor", workbench.StatusBar.DisplayedFocus);
    }

    [Fact]
    public async Task Diff_Next_change_moves_the_diff_the_menu_was_opened_from()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\nthree\nfour\nfive\nsix\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\ntwo\nthree\nFOUR\nfive\nSIX\n"; },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.StatusBar.DisplayedFocus == "Diff",
            () => host.App.InjectKey(Key.D.WithAlt),
            () => Focused(workbench) == "Compare to saved",
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => Focused(workbench) == "Next change",
            () => host.App.InjectKey(Key.Enter),
            () => !workbench.MenuBar.IsOpen() && workbench.Editor.Group.ActiveDiffTab?.CurrentChange == 1);

        Assert.Equal("Diff", workbench.StatusBar.DisplayedFocus);
    }

    // Move line down's key does nothing in the explorer, so neither does picking it there.
    [Fact]
    public async Task An_editor_command_picked_from_the_explorer_does_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.E.WithAlt),
            () => Focused(workbench) == "Move line up",
            () => host.App.InjectKey(Key.CursorDown),
            () => Focused(workbench) == "Move line down",
            () => host.App.InjectKey(Key.Enter),
            () => !workbench.MenuBar.IsOpen());

        Assert.Equal("one\ntwo\nthree\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
        Assert.Equal("Explorer", workbench.StatusBar.DisplayedFocus);
    }

    [Fact]
    public async Task Clicking_a_title_opens_it_clicking_an_item_runs_it_and_clicking_outside_closes_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            Click(host, () => Title(workbench, "View").FrameToScreen().Location),
            () => workbench.MenuBar.IsOpen(),
            Click(host, () => new Point(workbench.FrameToScreen().Width - 2, workbench.FrameToScreen().Height - 3)),
            () => !workbench.MenuBar.IsOpen(),
            Click(host, () => Title(workbench, "View").FrameToScreen().Location),
            () => workbench.MenuBar.IsOpen(),
            Click(host, () => Item(host, CommandIds.ToggleSidebar).FrameToScreen().Location),
            () => !workbench.MenuBar.IsOpen() && !workbench.IsSidebarVisible);
    }

    // Running a command under a dialog would take the keys out from under it.
    [Fact]
    public async Task The_menu_stays_shut_while_a_dialog_is_open()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => workbench.HasDialog,
            Click(host, () => Title(workbench, "File").FrameToScreen().Location),
            () => { },
            () => Assert.False(workbench.MenuBar.IsOpen()),
            () => host.App.InjectKey(Key.Esc),
            () => !workbench.HasDialog);
    }

    [Fact]
    public void Each_item_shows_its_current_key_and_follows_a_rebind()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        Assert.Equal("Alt+↓", KeyShown(host, CommandIds.MoveLinesDown));
        Assert.Equal("Ctrl+G l", KeyShown(host, CommandIds.GoToLine));
        Assert.Equal("", KeyShown(host, CommandIds.ToggleSidebar));

        host.ApplyKeybindings(
        [
            new KeybindingOverride(TestKeys.Chord("Alt+CursorDown"), "-" + CommandIds.MoveLinesDown),
            new KeybindingOverride(TestKeys.Chord("F7"), CommandIds.MoveLinesDown),
            new KeybindingOverride(TestKeys.Chord("Ctrl+G L"), "-" + CommandIds.GoToLine),
        ]);

        Assert.Equal("F7", KeyShown(host, CommandIds.MoveLinesDown));
        Assert.Equal("", KeyShown(host, CommandIds.GoToLine));
    }

    [Fact]
    public void Focus_editor_tab_opens_a_submenu_of_tabs_one_to_nine()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var go = Title(workbench, "Go").PopoverMenu!.Root!;
        var parent = go.SubViews.OfType<MenuItem>().Single(i => i.Title == CommandMenu.FocusEditorTab);

        Assert.Equal(CommandMenu.FocusEditorTabIds,
            parent.SubMenu!.SubViews.OfType<MenuItem>().Select(i => Id(host, i)));
    }

    [Fact]
    public void The_bar_takes_the_themes_menu_colours_and_stands_apart_from_the_explorer()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        ConfigurationManager.Enable(ConfigLocations.None);
        try
        {
            ConfigurationManager.RuntimeConfig = BundledThemes.Config;
            ConfigurationManager.Load(ConfigLocations.LibraryResources | ConfigLocations.Runtime);
            var drawn = new List<Attribute>();
            foreach (var theme in BundledThemes.Names)
            {
                ThemeManager.Theme = theme;
                ConfigurationManager.Apply();
                var menu = SchemeManager.GetScheme(Schemes.Menu).GetAttributeForRole(VisualRole.Normal, null);
                var sidebar = SchemeManager.GetScheme("Sidebar").GetAttributeForRole(VisualRole.Normal, null);
                Assert.Equal(menu, workbench.MenuBar.GetAttributeForRole(VisualRole.Normal));
                Assert.NotEqual(sidebar.Background, menu.Background);
                drawn.Add(menu);
            }
            Assert.True(drawn.Distinct().Count() > 1);
        }
        finally
        {
            ThemeManager.Theme = "Default";
            ConfigurationManager.Disable(resetToHardCodedDefaults: true);
        }
    }

    private static IEnumerable<string> MenuIds() =>
        CommandMenu.Layout.SelectMany(menu => menu.Ids)
            .Where(id => id != CommandMenu.Separator)
            .SelectMany(id => id == CommandMenu.FocusEditorTab ? CommandMenu.FocusEditorTabIds : [id]);

    private static string? Focused(Workbench.Workbench workbench) =>
        workbench.MenuBar.SubViews.OfType<MenuBarItem>().FirstOrDefault(m => m.PopoverMenuOpen)?.PopoverMenu?.Root?.Focused is MenuItem item
            ? item.Title
            : null;

    private static MenuBarItem Title(Workbench.Workbench workbench, string title) =>
        workbench.MenuBar.SubViews.OfType<MenuBarItem>().Single(m => m.Title.Replace("_", "") == title);

    private static MenuItem Item(WorkbenchHost host, string id) => host.Menu.Items.Single(i => i.Id == id).Item;

    private static string Id(WorkbenchHost host, MenuItem item) => host.Menu.Items.Single(i => i.Item == item).Id;

    private static string KeyShown(WorkbenchHost host, string id) => Item(host, id).KeyView.Text;

    private static Action Click(WorkbenchHost host, Func<Point> at) => () =>
    {
        var point = at();
        host.App.InjectMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, ScreenPosition = point });
        host.App.InjectMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, ScreenPosition = point });
    };

    private Workbench.Workbench BuildWorkbench(string root = "/work")
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        var workbench = new Workbench.Workbench(sidebar, new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New(root));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
    }
}
