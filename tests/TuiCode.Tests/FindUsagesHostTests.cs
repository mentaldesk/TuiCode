using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Usages;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `gu` / Ctrl+G U (#455) through the host against a fake language server. Boots a TG Application — serialised (#77).
public class FindUsagesHostTests : StaticConfigurationTest
{
    private const string Caller = """
        class Caller
        {
            void Go()
            {
                var hunks = Diff.Hunks();
                var count = hunks.Count;
            }
        }
        """;

    private const string Diff = "class Diff\n{\n    int Hunks() => 0;\n    int Twice() => Hunks() * 2;\n}\n";

    private const string Report = "class Report\n{\n    void Print()\n    {\n        Diff.Hunks();\n        Diff.Hunks();\n    }\n}\n";

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _server = new();

    public FindUsagesHostTests()
    {
        Add("/work/Caller.cs", Caller);
        Add("/work/Diff.cs", Diff);
        Add("/work/Report.cs", Report);
    }

    [Fact]
    public async Task Ctrl_G_U_lists_the_usages_by_file_in_the_Usages_tab_with_the_first_one_selected()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pane = workbench.Sidebar.Usages;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); Execute(host); },
            () => pane.Files.Count > 0 && pane.ResultsHaveFocus);

        Assert.Equal(SidebarTab.Usages, workbench.Sidebar.ActiveTab);
        Assert.Equal("Usages of Diff.Hunks  4 in 3 files", pane.HeaderText);
        Assert.Equal([("Caller.cs", 1), ("Diff.cs", 1), ("Report.cs", 2)], pane.Files.Select(f => (f.Name, f.Count)));
        Assert.Equal(
            ["5  var hunks = Diff.Hunks();", "4  int Twice() => Hunks() * 2;", "5  Diff.Hunks();", "6  Diff.Hunks();"],
            pane.Files.SelectMany(f => f.Children).Select(u => u.ToString()));
        Assert.Same(pane.Files[0].Children[0], pane.Tree.SelectedObject);
        Assert.Contains(_server.Received("textDocument/references"), p => p!["context"]!["includeDeclaration"]!.GetValue<bool>() == false);
    }

    [Fact]
    public async Task Enter_on_a_usage_opens_its_file_with_the_caret_on_it_and_gp_goes_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pane = workbench.Sidebar.Usages;
        (string File, int Row, int Column) jumped = default, back = default;
        var tabs = 0;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); Execute(host); },
            () => pane.Files.Count > 0 && pane.ResultsHaveFocus,
            () => { for (var i = 0; i < 4; i++) host.App.InjectKey(Key.CursorDown); },
            () => host.App.InjectKey(Key.Enter),
            () => Tab(workbench).File.Name == "Report.cs",
            () => { jumped = Where(workbench); tabs = workbench.Editor.Group.Tabs.Count(); host.App.InjectKey(Key.G.WithCtrl); },
            () => host.App.InjectKey(Key.P),
            () => { back = Where(workbench); });

        Assert.Equal(("Report.cs", 4, 13), jumped);
        Assert.Equal(2, tabs);
        Assert.Equal(("Caller.cs", 4, 26), back);
        Assert.Equal(SidebarTab.Usages, workbench.Sidebar.ActiveTab);
    }

    [Fact]
    public async Task A_Find_search_leaves_the_usages_alone_and_a_new_Ctrl_G_U_replaces_them()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pane = workbench.Sidebar.Usages;
        string afterFind = "", afterSecond = "";
        var foundInFind = 0;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); Execute(host); },
            () => pane.Files.Count > 0,
            () => { workbench.Sidebar.Search.Query = "Hunks"; host.App.InjectKey(Key.F.WithCtrl.WithShift); },
            () => workbench.Sidebar.Search.Result.MatchCount > 0,
            () => { foundInFind = workbench.Sidebar.Search.Result.MatchCount; afterFind = pane.HeaderText; },
            () => host.App.InjectKey(Key.Esc),
            () => { Tab(workbench).MoveCursor(2, 9); Execute(host); },
            () => pane.HeaderText.StartsWith("No usages", StringComparison.Ordinal),
            () => { afterSecond = pane.HeaderText; });

        Assert.True(foundInFind > 0);
        Assert.Equal("Usages of Diff.Hunks  4 in 3 files", afterFind);
        Assert.Equal("No usages of Caller.Go", afterSecond);
        Assert.Empty(pane.Files);
    }

    [Fact]
    public async Task Ctrl_Shift_U_goes_back_to_the_usages_without_finding_them_again()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pane = workbench.Sidebar.Usages;
        var region = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); Execute(host); },
            () => pane.Files.Count > 0 && pane.ResultsHaveFocus,
            () => host.App.InjectKey(Key.Esc),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { Tab(workbench).MoveCursor(2, 9); host.App.InjectKey(Key.U.WithCtrl.WithShift); },
            () => pane.ResultsHaveFocus,
            () => { region = workbench.StatusBar.DisplayedFocus; });

        Assert.Equal("Usages", region);
        Assert.Equal("Usages of Diff.Hunks  4 in 3 files", pane.HeaderText);
        Assert.Single(_server.Received("textDocument/references"));
    }

    [Fact]
    public async Task While_the_server_works_the_tab_says_so_and_the_menu_item_is_dimmed_until_it_is_ready()
    {
        using var hold = new ManualResetEventSlim();
        _server.HoldReferences = hold;
        _server.HoldLoading = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pane = workbench.Sidebar.Usages;
        string working = "";
        bool dimmedWhileLoading = false, dimmedWhenReady = true;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => workbench.StatusBar.DisplayedText.EndsWith("C# ◌ loading", StringComparison.Ordinal),
            () => host.App.InjectKey(Key.G.WithAlt),
            () => workbench.MenuBar.IsOpen(),
            () => { dimmedWhileLoading = Item(host).Dimmed; host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen(),
            () => { _server.FinishLoading(); },
            () => Ready(workbench),
            () => host.App.InjectKey(Key.G.WithAlt),
            () => workbench.MenuBar.IsOpen(),
            () => { dimmedWhenReady = Item(host).Dimmed; host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen(),
            () => { Tab(workbench).MoveCursor(4, 26); Execute(host); },
            () => workbench.Sidebar.ActiveTab == SidebarTab.Usages,
            () => { working = pane.HeaderText; hold.Set(); },
            () => pane.Files.Count > 0);

        Assert.True(dimmedWhileLoading);
        Assert.False(dimmedWhenReady);
        Assert.Equal("Finding usages of Hunks…", working);
        Assert.Equal("Usages of Diff.Hunks  4 in 3 files", pane.HeaderText);
    }

    [Fact]
    public async Task Without_the_server_installed_Ctrl_G_U_says_how_to_install_it()
    {
        _server.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var shown = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => { workbench.StatusBar.SetMessage("elsewhere"); Tab(workbench).MoveCursor(4, 26); Execute(host); },
            () => { shown = workbench.StatusBar.Message; });

        Assert.Equal("No C# language server. Install: dotnet tool install -g csharp-ls", shown);
        Assert.Equal(SidebarTab.Explorer, workbench.Sidebar.ActiveTab);
    }

    [Fact]
    public async Task F1_in_the_Usages_tab_lists_its_keys()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        HelpView? help = null;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); Execute(host); },
            () => workbench.Sidebar.Usages.ResultsHaveFocus,
            () => host.App.InjectKey(Key.F1),
            () => (help = workbench.SubViews.OfType<HelpView>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("Usages", help!.Place!.Title);
        Assert.Equal(
        [
            new("Enter", "Go to the usage"),
            new("↑ ↓", "Step through usages"),
            new("→ ←", "Expand or collapse"),
        ], help.Place.Rows);
    }

    [Fact]
    public void Gu_is_on_Ctrl_G_U_in_the_editor_with_a_mnemonic_and_a_place_in_the_Go_menu_after_Go_to_definition()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Find usages", commands.Registered.Single(c => c.Id == CommandIds.FindUsages).Label);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.FindUsages));
        Assert.Equal("gu", CommandMnemonics.For(CommandIds.FindUsages));
        Assert.Equal("Ctrl+G u", keybindings.Bindings.Single(b => b.CommandId == CommandIds.FindUsages).Display);
        var go = host.Menu.Items.Select(i => i.Id).ToList();
        Assert.Equal(go.IndexOf(CommandIds.GoToDefinition) + 1, go.IndexOf(CommandIds.FindUsages));
    }

    [Fact]
    public void Focus_usages_is_on_Ctrl_Shift_U_with_a_mnemonic_and_a_place_in_the_View_menu_before_Focus_review()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Focus usages", commands.Registered.Single(c => c.Id == CommandIds.FocusUsages).Label);
        Assert.Equal(CommandScope.Global, commands.ScopeOf(CommandIds.FocusUsages));
        Assert.Equal("fu", CommandMnemonics.For(CommandIds.FocusUsages));
        Assert.Equal("Ctrl+Shift+U", keybindings.Bindings.Single(b => b.CommandId == CommandIds.FocusUsages).Display);
        var items = host.Menu.Items.Select(i => i.Id).ToList();
        Assert.Equal(items.IndexOf(CommandIds.FocusReview) - 1, items.IndexOf(CommandIds.FocusUsages));
    }

    private void Add(string path, string content)
    {
        _fs.AddFile(path, new MockFileData(content));
        _server.Files[_fs.Path.GetFullPath(path)] = content;
    }

    private static bool Ready(Workbench.Workbench workbench) =>
        workbench.StatusBar.DisplayedText.EndsWith("C# ● ready", StringComparison.Ordinal);

    private static void Execute(WorkbenchHost host)
    {
        host.App.InjectKey(Key.G.WithCtrl);
        host.App.InjectKey(Key.U);
    }

    private static (string File, int Row, int Column) Where(Workbench.Workbench workbench) =>
        (Tab(workbench).File.Name, Tab(workbench).CursorRow, Tab(workbench).CursorColumn);

    private static TuiCode.Workbench.Menus.CommandMenuItem Item(WorkbenchHost host) =>
        host.Menu.Items.Single(i => i.Id == CommandIds.FindUsages).Item;

    private void OpenFile(Workbench.Workbench workbench, string name) =>
        workbench.OpenFile(_fs.FileInfo.New($"/work/{name}"));

    private static TuiCode.Editor.EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        workbench.Sidebar.Search.RootProvider = () => workbench.Sidebar.Explorer.Root;
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, languageServers: _server);
        HostSteps.PinScreenSize(host, 80, 25);
        return host;
    }
}
