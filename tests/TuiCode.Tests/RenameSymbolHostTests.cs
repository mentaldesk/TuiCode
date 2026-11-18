using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Theming;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Rename;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `rs` (#697) through the host against a fake language server. Boots a TG Application — serialised (#77).
public class RenameSymbolHostTests : StaticConfigurationTest
{
    private const string Diff = "class Diff\n{\n    int Hunks() => 0;\n    // Hunks are counted here\n    int Twice() => Hunks() * 2;\n    string Name = \"Hunks\";\n}\n";

    private const string Report = "class Report\r\n{\r\n    void Print()\r\n    {\r\n        Diff.Hunks();\r\n    }\r\n}\r\n";

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private static readonly string[] Renamed =
        ["class Diff", "{", "    int Count() => 0;", "    // Hunks are counted here", "    int Twice() => Count() * 2;", "    string Name = \"Hunks\";", "}", ""];

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _server = new();

    public RenameSymbolHostTests()
    {
        Add("/work/Diff.cs", Diff);
        Add("/work/Report.cs", Report);
    }

    [Fact]
    public async Task Rs_on_a_method_opens_Rename_with_the_name_all_selected()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        RenameSymbolView? view = null;
        string selected = "", label = "";

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { workbench.Editor.Group.ActiveTab!.MoveCursor(2, 10); Rs(host); },
            () => (view = Dialog(workbench)) is not null,
            () => { selected = view!.SelectedText; label = view.FieldLabel.Text; host.App.InjectKey(Key.Esc); },
            () => Dialog(workbench) is null);

        Assert.Equal("Rename Hunks", view!.Title);
        Assert.Equal("New name:", label);
        Assert.Equal("Hunks", view.NewName);
        Assert.Equal("Hunks", selected);
        Assert.Single(_server.Received("textDocument/prepareRename"));
    }

    [Fact]
    public async Task With_every_file_open_Ctrl_Enter_renames_in_the_tabs_unsaved_and_Ctrl_Z_takes_a_tab_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        ConfirmDialog? confirm = null;
        string[] renamed = [], undone = [];

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Report.cs"),
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { Tab(workbench, "Diff.cs").MoveCursor(2, 10); Rs(host); },
            () => Dialog(workbench) is not null,
            () => Submit(host, workbench, "Count"),
            () => Dialog(workbench) is null && workbench.Sidebar.Usages.HeaderText.StartsWith("Renamed", StringComparison.Ordinal),
            () => { confirm = ConfirmDialogs.Of(workbench); renamed = [.. Tab(workbench, "Diff.cs").Lines]; },
            () => host.App.InjectKey(Key.Esc),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.Z.WithCtrl),
            () => { undone = [.. Tab(workbench, "Diff.cs").Lines]; });

        Assert.Null(confirm);
        Assert.Equal(Renamed, renamed);
        Assert.Equal(Diff.Split('\n'), undone);
        Assert.Equal(["class Report", "{", "    void Print()", "    {", "        Diff.Count();", "    }", "}", ""], Tab(workbench, "Report.cs").Lines);
        Assert.True(Tab(workbench, "Report.cs").IsDirty);
        Assert.Equal(Report, _fs.File.ReadAllText("/work/Report.cs"));
        Assert.Equal(Diff, _fs.File.ReadAllText("/work/Diff.cs"));
        Assert.Equal("Renamed Hunks → Count · 3 in 2 files", workbench.Sidebar.Usages.HeaderText);
    }

    [Fact]
    public async Task With_a_file_not_open_a_confirm_says_so_first_and_Cancel_changes_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        ConfirmDialog? confirm = null;
        string[] buttons = [];
        string? focused = null;

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { Tab(workbench, "Diff.cs").MoveCursor(2, 10); Rs(host); },
            () => Dialog(workbench) is not null,
            () => Submit(host, workbench, "Count"),
            () => (confirm = ConfirmDialogs.Of(workbench)) is not null,
            () => { buttons = confirm!.Buttons(); focused = confirm.FocusedButton(); host.App.InjectKey(Key.Esc); },
            () => ConfirmDialogs.Of(workbench) is null);

        Assert.Equal("Rename symbol", confirm!.Title);
        Assert.Equal("Rename Hunks to Count?\n3 changes in 2 files. 1 file isn't open and is saved straight away.", confirm.Message());
        Assert.Equal(["Ctrl+Enter Rename", "Esc Cancel"], buttons);
        Assert.Equal("Esc Cancel", focused);
        Assert.Equal(Diff.Split('\n'), Tab(workbench, "Diff.cs").Lines);
        Assert.False(Tab(workbench, "Diff.cs").IsDirty);
        Assert.Equal(Report, _fs.File.ReadAllText("/work/Report.cs"));
        Assert.DoesNotContain("Renamed", workbench.Sidebar.Usages.HeaderText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_in_the_confirm_writes_the_closed_file_and_lists_the_changed_lines_in_Usages()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pane = workbench.Sidebar.Usages;
        (string File, int Row, int Column) jumped = default;

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { Tab(workbench, "Diff.cs").MoveCursor(2, 10); Rs(host); },
            () => Dialog(workbench) is not null,
            () => Submit(host, workbench, "Count"),
            () => ConfirmDialogs.Of(workbench) is not null,
            ConfirmDialogs.Choose(host, workbench, "Rename"),
            () => pane.Files.Count > 0 && pane.ResultsHaveFocus,
            () => { for (var i = 0; i < 3; i++) host.App.InjectKey(Key.CursorDown); },
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveTab!.File.Name == "Report.cs",
            () =>
            {
                var tab = workbench.Editor.Group.ActiveTab!;
                jumped = (tab.File.Name, tab.CursorRow, tab.CursorColumn);
            });

        Assert.Equal("class Report\r\n{\r\n    void Print()\r\n    {\r\n        Diff.Count();\r\n    }\r\n}\r\n", _fs.File.ReadAllText("/work/Report.cs"));
        Assert.Equal(Renamed, Tab(workbench, "Diff.cs").Lines);
        Assert.True(Tab(workbench, "Diff.cs").IsDirty);
        Assert.Equal(Diff, _fs.File.ReadAllText("/work/Diff.cs"));
        Assert.Equal("Renamed Hunks → Count · 3 in 2 files", pane.HeaderText);
        Assert.Equal([("Diff.cs", 2), ("Report.cs", 1)], pane.Files.Select(f => (f.Name, f.Count)));
        Assert.Equal(["3  int Count() => 0;", "5  int Twice() => Count() * 2;", "5  Diff.Count();"],
            pane.Files.SelectMany(f => f.Children).Select(u => u.ToString()));
        Assert.Equal(("Report.cs", 4, 13), jumped);
        Assert.Contains(_server.Received("workspace/didChangeWatchedFiles"),
            p => p!["changes"]![0]!["uri"]!.GetValue<string>() == TuiCode.Workbench.Languages.LspLocations.ToUri(_fs.Path.GetFullPath("/work/Report.cs")));
    }

    [Fact]
    public async Task Esc_closes_the_dialog_and_changes_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { Tab(workbench, "Diff.cs").MoveCursor(2, 10); Rs(host); },
            () => Dialog(workbench) is not null,
            () => { Field(workbench).Text = "Count"; host.App.InjectKey(Key.Esc); },
            () => Dialog(workbench) is null);

        Assert.Equal(Diff.Split('\n'), Tab(workbench, "Diff.cs").Lines);
        Assert.Empty(_server.Received("textDocument/rename"));
    }

    [Theory]
    [InlineData("", "New name is needed")]
    [InlineData("Hunks", "New name is still Hunks")]
    public async Task An_empty_or_unchanged_name_keeps_the_dialog_open_and_says_why(string typed, string why)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string said = "";
        string? marked = null;

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { Tab(workbench, "Diff.cs").MoveCursor(2, 10); Rs(host); },
            () => Dialog(workbench) is not null,
            () => Submit(host, workbench, typed),
            () => Dialog(workbench)!.Alert.Lines > 0,
            () => { said = Dialog(workbench)!.Alert.Message; marked = Dialog(workbench)!.FieldLabel.SchemeName; host.App.InjectKey(Key.Esc); },
            () => Dialog(workbench) is null);

        Assert.Equal(why, said);
        Assert.Equal(SchemeNames.Error, marked);
        Assert.Empty(_server.Received("textDocument/rename"));
    }

    [Fact]
    public async Task A_name_the_server_refuses_keeps_the_dialog_open_and_says_why_in_its_words()
    {
        _server.RenameError = "Twice is already defined in Diff";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string said = "";

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { Tab(workbench, "Diff.cs").MoveCursor(2, 10); Rs(host); },
            () => Dialog(workbench) is not null,
            () => Submit(host, workbench, "Twice"),
            () => Dialog(workbench)!.Alert.Lines > 0,
            () => { said = Dialog(workbench)!.Alert.Message; host.App.InjectKey(Key.Esc); },
            () => Dialog(workbench) is null);

        Assert.Equal("New name: Twice is already defined in Diff", said);
        Assert.Equal(Diff.Split('\n'), Tab(workbench, "Diff.cs").Lines);
    }

    [Fact]
    public async Task On_a_keyword_the_dialog_doesnt_open_and_the_status_bar_says_why()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var said = "";

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { Tab(workbench, "Diff.cs").MoveCursor(2, 5); Rs(host); },
            () => _server.Received("textDocument/prepareRename").Any() && workbench.StatusBar.Message == "Can't rename this",
            () => { said = workbench.StatusBar.Message; });

        Assert.Equal("Can't rename this", said);
        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task Where_prepareRename_refuses_the_status_bar_says_why_in_the_servers_words()
    {
        _server.PrepareRenameError = "it's defined in a library";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { Tab(workbench, "Diff.cs").MoveCursor(2, 10); Rs(host); },
            () => workbench.StatusBar.Message.StartsWith("Can't rename", StringComparison.Ordinal));

        Assert.Equal("Can't rename this: it's defined in a library", workbench.StatusBar.Message);
        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task With_a_server_that_doesnt_rename_the_status_bar_says_so()
    {
        _server.RenameProvider = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => Ready(workbench),
            () => { Tab(workbench, "Diff.cs").MoveCursor(2, 10); Rs(host); },
            () => workbench.StatusBar.Message.StartsWith("Can't rename", StringComparison.Ordinal));

        Assert.Equal("Can't rename this: the C# language server doesn't rename", workbench.StatusBar.Message);
        Assert.Null(Dialog(workbench));
        Assert.Empty(_server.Received("textDocument/prepareRename"));
    }

    [Fact]
    public async Task Without_the_server_installed_rs_says_how_to_install_it()
    {
        _server.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => { workbench.StatusBar.ShowMessage("elsewhere"); Rs(host); },
            () => workbench.StatusBar.Message != "elsewhere");

        Assert.Equal("No C# language server. Install: dotnet tool install -g csharp-ls", workbench.StatusBar.Message);
        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task While_the_server_loads_the_Edit_menu_dims_Rename_symbol()
    {
        _server.HoldLoading = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        bool dimmedWhileLoading = false, dimmedWhenReady = true;

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Diff.cs"),
            () => workbench.StatusBar.DisplayedState.Contains("C# ◌ loading", StringComparison.Ordinal),
            () => host.App.InjectKey(Key.E.WithAlt),
            () => workbench.MenuBar.IsOpen(),
            () => { dimmedWhileLoading = Item(host).Dimmed; host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen(),
            () => { _server.FinishLoading(); },
            () => Ready(workbench),
            () => host.App.InjectKey(Key.E.WithAlt),
            () => workbench.MenuBar.IsOpen(),
            () => { dimmedWhenReady = Item(host).Dimmed; host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen());

        Assert.True(dimmedWhileLoading);
        Assert.False(dimmedWhenReady);
    }

    [Fact]
    public void Rs_has_no_key_and_a_place_in_the_Edit_menu_after_Replace_globally()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Rename symbol", commands.Registered.Single(c => c.Id == CommandIds.RenameSymbol).Label);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.RenameSymbol));
        Assert.Equal("rs", CommandMnemonics.For(CommandIds.RenameSymbol));
        Assert.DoesNotContain(keybindings.Bindings, b => b.CommandId == CommandIds.RenameSymbol);
        var items = host.Menu.Items.Select(i => i.Id).ToList();
        Assert.Equal(items.IndexOf(CommandIds.ReplaceGlobally) + 1, items.IndexOf(CommandIds.RenameSymbol));
    }

    private void Add(string path, string content)
    {
        _fs.AddFile(path, new MockFileData(content));
        _server.Files[_fs.Path.GetFullPath(path)] = content;
    }

    private static bool Ready(Workbench.Workbench workbench) =>
        workbench.StatusBar.DisplayedState.Contains("C# ● ready", StringComparison.Ordinal);

    private static void Rs(WorkbenchHost host)
    {
        host.App.InjectKey(Key.Space.WithCtrl);
        host.App.InjectKey(Key.R);
        host.App.InjectKey(Key.S);
    }

    private static void Submit(WorkbenchHost host, Workbench.Workbench workbench, string name)
    {
        Field(workbench).Text = name;
        host.App.InjectKey(Key.Enter.WithCtrl);
    }

    private static RenameSymbolView? Dialog(Workbench.Workbench workbench) => workbench.SubViews.OfType<RenameSymbolView>().SingleOrDefault();

    private static TextField Field(Workbench.Workbench workbench) => Dialog(workbench)!.SubViews.OfType<TextField>().Single();

    private static TuiCode.Workbench.Menus.CommandMenuItem Item(WorkbenchHost host) =>
        host.Menu.Items.Single(i => i.Id == CommandIds.RenameSymbol).Item;

    private void OpenFile(Workbench.Workbench workbench, string name) =>
        workbench.OpenFile(_fs.FileInfo.New($"/work/{name}"));

    private static TuiCode.Editor.EditorTab Tab(Workbench.Workbench workbench, string name) =>
        workbench.Editor.Groups.Tabs.Single(t => t.File.Name == name);

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, languageServers: _server);
        HostSteps.PinScreenSize(host, 100, 30);
        return host;
    }
}
