using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Search;
using TuiCode.Workbench;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Match case, whole word and ignored files in both find boxes (#527). Boots a TG Application — serialised (#77).
public class FindOptionsHostTests : StaticConfigurationTest
{
    private static readonly Key AltC = Key.C.WithAlt;
    private static readonly Key AltW = Key.W.WithAlt;

    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();

    public FindOptionsHostTests()
    {
        _fs.AddFile("/work/a.cs", new MockFileData("Id id Hidden\n"));
        _fs.AddFile("/work/b.cs", new MockFileData("Id\n"));
    }

    [Fact]
    public async Task Alt_C_and_Alt_W_in_the_find_bar_toggle_the_options_and_search_again()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        FindBarView? bar = null;
        var counts = new List<string>();

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.cs")),
            () => { workbench.Editor.Group.ActiveTab!.FocusContent(); host.App.InjectKey(Key.F.WithCtrl); },
            () => (bar = workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault()) is { HasFocus: true },
            () => { foreach (var c in "id") host.App.InjectKey(new Key(c)); },
            () => { counts.Add(bar!.Status); host.App.InjectKey(AltC); },
            () => { counts.Add(bar!.Status); host.App.InjectKey(AltC); },
            () => { counts.Add(bar!.Status); host.App.InjectKey(AltW); },
            () => { counts.Add(bar!.Status); host.App.InjectKey(new Key('x')); });

        Assert.Equal(["1 of 3", "1 of 2", "2 of 3", "2 of 2"], counts);
        Assert.Equal(new MatchOptions(WholeWord: true), bar!.Options);
        Assert.Equal("idx", bar.Query);
    }

    [Fact]
    public async Task The_find_bars_options_are_shared_by_every_tab_and_outlast_closing_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        FindBarView? bar = null;
        var options = default(MatchOptions);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.cs")),
            () => { workbench.Editor.Group.ActiveTab!.FocusContent(); host.App.InjectKey(Key.F.WithCtrl); },
            () => (bar = workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault()) is { HasFocus: true },
            () => { host.App.InjectKey(AltC); host.App.InjectKey(Key.Esc); },
            () => !workbench.SubViewsDeep().OfType<FindBarView>().Any(),
            () => workbench.OpenFile(_fs.FileInfo.New("/work/b.cs")),
            () => { workbench.Editor.Group.ActiveTab!.FocusContent(); host.App.InjectKey(Key.F.WithCtrl); },
            () => workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault() is { HasFocus: true },
            () => { options = bar!.Options; return true; });

        Assert.Equal(new MatchOptions(MatchCase: true), options);
    }

    [Fact]
    public async Task The_pane_has_its_own_options_apart_from_the_bars()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var search = workbench.Sidebar.Search;
        FindBarView? bar = null;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.cs")),
            () => { workbench.Editor.Group.ActiveTab!.FocusContent(); host.App.InjectKey(Key.F.WithCtrl); },
            () => (bar = workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault()) is { HasFocus: true },
            () => host.App.InjectKey(AltC),
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => search.InputsHaveFocus,
            () => { foreach (var c in "Id") host.App.InjectKey(new Key(c)); },
            () => search.Result.MatchCount == 4,
            () => host.App.InjectKey(AltW),
            () => search.Result.MatchCount == 3);

        Assert.Equal(new MatchOptions(WholeWord: true), search.Options);
        Assert.Equal(new MatchOptions(MatchCase: true), bar!.Options);
    }

    [Fact]
    public async Task Typing_c_and_w_in_either_find_box_types_them_rather_than_toggling()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var search = workbench.Sidebar.Search;
        FindBarView? bar = null;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.cs")),
            () => { workbench.Editor.Group.ActiveTab!.FocusContent(); host.App.InjectKey(Key.F.WithCtrl); },
            () => (bar = workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault()) is { HasFocus: true },
            () => { foreach (var c in "cwCW") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => search.InputsHaveFocus,
            () => { foreach (var c in "cwCW") host.App.InjectKey(new Key(c)); });

        Assert.Equal("cwCW", bar!.Query);
        Assert.Equal(default, bar.Options);
        Assert.Equal("cwCW", search.Query);
        Assert.Equal(default, search.Options);
    }

    [Fact]
    public async Task Away_from_the_find_boxes_Alt_C_and_Alt_W_toggle_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var enabled = true;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.cs")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { enabled = _commands.IsEnabled(CommandIds.ToggleMatchCase) || _commands.IsEnabled(CommandIds.ToggleWholeWord); return true; },
            () => { host.App.InjectKey(AltC); host.App.InjectKey(AltW); });

        Assert.False(enabled);
        Assert.Equal(default, workbench.Sidebar.Search.Options);
        Assert.False(host.Menu.IsOpen);
    }

    [Fact]
    public async Task The_palette_names_each_option_for_what_it_will_do_in_the_box_it_opened_from()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> before = [], after = [];

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => workbench.Sidebar.Search.InputsHaveFocus,
            () => { before = Palette("match"); return true; },
            () => { host.App.InjectKey(AltC); host.App.InjectKey(AltW); },
            () => { after = Palette("match"); return true; });

        Assert.Equal(["Match case", "Match whole word"], before);
        Assert.Equal(["Stop matching case", "Stop matching whole word"], after);
    }

    [Fact]
    public async Task Outside_a_git_repo_ignored_files_is_not_offered()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> offered = [];

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => workbench.Sidebar.Search.InputsHaveFocus,
            () => { offered = Palette("ignored"); return true; });

        Assert.Empty(offered);
        Assert.False(_commands.IsEnabled(CommandIds.ToggleIgnoredFiles));
    }

    [Fact]
    public async Task In_a_git_repo_tfi_brings_back_the_files_git_ignores()
    {
        var git = new FakeGitCli { ListedFiles = ignored => ignored ? ["a.cs", "b.cs"] : ["a.cs"] };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, git);
        var search = workbench.Sidebar.Search;
        string? title = null;

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => search.InputsHaveFocus,
            () => { workbench.Sidebar.Search.InRepository = _ => true; foreach (var c in "Id") host.App.InjectKey(new Key(c)); },
            () => search.Result.MatchCount == 3,
            () => { title = Palette("ignored").Single(); return _commands.TryExecute(CommandIds.ToggleIgnoredFiles); },
            () => search.Result.MatchCount == 4);

        Assert.Equal("Include ignored files", title);
        Assert.Equal("Skip ignored files", Palette("ignored").Single());
    }

    [Fact]
    public async Task F1_in_the_find_pane_lists_the_option_keys()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        KeysDialog? help = null;

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => workbench.Sidebar.Search.InputsHaveFocus,
            () => host.App.InjectKey(Key.F1),
            () => (help = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));

        Assert.Contains(new KeyRow("Alt+C", "Match case on or off"), help!.Sheet.Here!.Rows());
        Assert.Contains(new KeyRow("Alt+W", "Match whole word on or off"), help.Sheet.Here!.Rows());
    }

    private IReadOnlyList<string> Palette(string query)
    {
        using var view = new ActionView(_commands, new KeybindingService(_commands), CommandScope.Find, _ => { });
        view.SubViews.OfType<TextField>().Single().Text = query;
        return view.Labels;
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, IGitCli? git = null) =>
        new(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: git);
}
