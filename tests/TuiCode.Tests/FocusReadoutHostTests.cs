using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Themes;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Tests;

// The status-bar word and the focused pane's border, driven through real keys (#227).
// Boots a TG Application — serialised (#77).
public class FocusReadoutHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();

    public FocusReadoutHostTests()
    {
        _fs.AddDirectory("/work");
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("three\n"));
    }

    [Fact]
    public async Task The_focus_commands_move_the_word_with_the_keys()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var words = new List<string>();

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { words.Add(workbench.StatusBar.DisplayedFocus); commands.TryExecute(CommandIds.FocusSidebar); },
            () => { words.Add(workbench.StatusBar.DisplayedFocus); commands.TryExecute(CommandIds.FindGlobally); },
            () => { words.Add(workbench.StatusBar.DisplayedFocus); commands.TryExecute(CommandIds.FocusReview); },
            () => { words.Add(workbench.StatusBar.DisplayedFocus); host.App.InjectKey(Key.Esc); },
            () => { words.Add(workbench.StatusBar.DisplayedFocus); commands.TryExecute(CommandIds.FocusEditorTabStrip); },
            () => words.Add(workbench.StatusBar.DisplayedFocus));

        Assert.Equal(["Editor", "Explorer", "Find", "Review", "Editor", "Tabs"], words);
    }

    // The strip is a mode over the focused editor: the word stays on Tabs while its keys cycle tabs.
    [Fact]
    public async Task Cycling_tabs_from_the_strip_keeps_the_word_on_Tabs_and_Enter_returns_to_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var afterCycling = "";

        await HostSteps.Run(host,
            () => { workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")); workbench.OpenFile(_fs.FileInfo.New("/work/b.txt")); },
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => commands.TryExecute(CommandIds.FocusEditorTabStrip),
            () => host.App.InjectKey(Key.CursorLeft),
            () => { afterCycling = workbench.StatusBar.DisplayedFocus; host.App.InjectKey(Key.Enter); },
            () => workbench.StatusBar.DisplayedFocus == "Editor");

        Assert.Equal("Tabs", afterCycling);
        Assert.Equal("a.txt", workbench.Editor.Group.ActiveTab?.File.Name);
    }

    [Fact]
    public async Task Toggling_the_sidebar_moves_the_word_between_the_explorer_and_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var hidden = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => commands.TryExecute(CommandIds.ToggleSidebar),
            () => { hidden = workbench.StatusBar.DisplayedFocus; commands.TryExecute(CommandIds.ToggleSidebar); },
            () => workbench.StatusBar.DisplayedFocus == "Explorer");

        Assert.Equal("Editor", hidden);
    }

    // Esc from the sidebar doesn't reach the diff today (#197, slice 2); the readout is what makes that visible.
    [Fact]
    public async Task An_active_diff_reads_as_Diff_and_the_sidebar_takes_the_word_from_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var onDiff = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\nTWO\n"; },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.Editor.Group.ActiveDiffTab is not null,
            () => { onDiff = workbench.StatusBar.DisplayedFocus; commands.TryExecute(CommandIds.FocusSidebar); },
            () => workbench.StatusBar.DisplayedFocus == "Explorer");

        Assert.Equal("Diff", onDiff);
    }

    // Nothing else routes focus through FocusService, so the reconcile is what keeps the word honest.
    [Fact]
    public async Task A_focus_move_Terminal_Gui_makes_on_its_own_is_picked_up_by_the_next_iteration()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var beforeIteration = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () =>
            {
                workbench.Sidebar.Explorer.SetFocus();
                beforeIteration = workbench.StatusBar.DisplayedFocus;
            },
            () => workbench.StatusBar.DisplayedFocus == "Explorer");

        Assert.Equal("Editor", beforeIteration);
    }

    [Fact]
    public async Task The_focused_panes_border_is_drawn_in_the_focus_colour_and_the_other_stays_normal()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        (Attribute? Sidebar, Attribute? Editor) onEditor = default;
        (Attribute? Sidebar, Attribute? Editor) onExplorer = default;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { onEditor = Borders(host, workbench); },
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => { onExplorer = Borders(host, workbench); });

        Assert.Equal(workbench.Editor.GetAttributeForRole(VisualRole.Focus), onEditor.Editor);
        Assert.Equal(workbench.Sidebar.GetAttributeForRole(VisualRole.Normal), onEditor.Sidebar);
        Assert.Equal(workbench.Sidebar.GetAttributeForRole(VisualRole.Focus), onExplorer.Sidebar);
        Assert.Equal(workbench.Editor.GetAttributeForRole(VisualRole.Normal), onExplorer.Editor);
    }

    // The word is legible without colour; the border needs the theme's focus attribute to differ from normal.
    [Fact]
    public void Every_bundled_theme_draws_a_focused_pane_differently_from_an_unfocused_one()
    {
        ConfigurationManager.Enable(ConfigLocations.None);
        try
        {
            ConfigurationManager.RuntimeConfig = BundledThemes.Config;
            ConfigurationManager.Load(ConfigLocations.LibraryResources | ConfigLocations.Runtime);

            foreach (var theme in BundledThemes.Names)
            {
                ThemeManager.Theme = theme;
                ConfigurationManager.Apply();
                foreach (var name in new[] { "Base", "Sidebar" })
                {
                    Assert.True(SchemeManager.TryGetScheme(name, out var scheme));
                    var normal = scheme!.GetAttributeForRole(VisualRole.Normal, null);
                    var focus = scheme.GetAttributeForRole(VisualRole.Focus, null);
                    Assert.True(normal != focus, $"{theme}'s {name} draws a focused border like an unfocused one");
                    Assert.True(focus.Foreground != focus.Background, $"{theme}'s {name} focus colour is unreadable");
                }
            }
        }
        finally
        {
            ThemeManager.Theme = "Default";
            ConfigurationManager.Disable(resetToHardCodedDefaults: true);
        }
    }

    private static (Attribute? Sidebar, Attribute? Editor) Borders(WorkbenchHost host, Workbench.Workbench workbench) =>
        (At(host, workbench.Sidebar.FrameToScreen()), At(host, workbench.Editor.FrameToScreen()));

    private static Attribute? At(WorkbenchHost host, System.Drawing.Rectangle frame) =>
        host.App.Driver?.Contents?[frame.Y, frame.X].Attribute;

    private Workbench.Workbench BuildWorkbench()
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        var workbench = new Workbench.Workbench(sidebar, new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
    }
}
