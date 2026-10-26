using System.Text.Json.Nodes;
using MentalDesk.Tui.Keys;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `gi` / Ctrl+G I (#591) through the host against a fake language server. Boots a TG Application — serialised (#77).
public class QuickInfoHostTests : StaticConfigurationTest
{
    private const string Caller = """
        class Caller
        {
            void Go()
            {
                var hunks = Diff.Hunks();
                var widget = new Widget();
                var count = hunks.Count;
            }
        }
        """;

    private static readonly string Diff = "class Diff\n{\n" + string.Concat(Enumerable.Repeat("    // padding\n", 40)) + "    int Hunks() => 0;\n}\n";

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _server = new();

    public QuickInfoHostTests()
    {
        Add("/work/Caller.cs", Caller);
        Add("/work/Diff.cs", Diff);
        Add("/work/Widget.cs", "class Widget\n{\n}\n");
        _fs.AddFile("/work/README.md", new MockFileData("# Read me"));
    }

    [Fact]
    public async Task Ctrl_G_I_shows_the_signature_and_doc_under_the_line_and_Esc_closes_it_leaving_the_caret()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        QuickInfoView? box = null;
        IReadOnlyList<string> rows = [];
        var caretY = 0;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); },
            () => { caretY = Tab(workbench).CaretOnScreen!.Value.Y; Execute(host); },
            () => (box = Box(workbench)) is not null,
            () => { rows = box!.Rows; host.App.InjectKey(Key.Esc); },
            () => Box(workbench) is null);

        Assert.Equal("Quick info", box!.Title);
        Assert.Equal(["int Hunks() => 0;", "", "About Hunks."], rows);
        Assert.Equal(caretY + 1, box.FrameToScreen().Y);
        Assert.Equal(("Caller.cs", 4, 26), Where(workbench));
        Assert.Contains("textDocument/hover", _server.Methods);
    }

    [Fact]
    public async Task Enter_closes_it_and_goes_to_the_definition_and_Back_returns()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        (string, int, int) jumped = default;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); },
            () => { Execute(host); },
            () => Box(workbench) is not null,
            () => host.App.InjectKey(Key.Enter),
            () => Tab(workbench).File.Name == "Diff.cs",
            () => { jumped = Where(workbench); host.App.InjectKey(Key.G.WithCtrl); },
            () => host.App.InjectKey(Key.P),
            () => Tab(workbench).File.Name == "Caller.cs");

        Assert.Null(Box(workbench));
        Assert.Equal(("Diff.cs", 42, 8), jumped);
        Assert.Equal(("Caller.cs", 4, 26), Where(workbench));
    }

    [Fact]
    public async Task Its_buttons_do_what_their_keys_do()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        QuickInfoView? box = null;
        string close = "", goTo = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); },
            () => { Execute(host); },
            () => (box = Box(workbench)) is not null,
            () => { close = box!.CloseButton.Text; box.CloseButton.InvokeCommand(Command.Accept); },
            () => Box(workbench) is null,
            () => { Execute(host); },
            () => (box = Box(workbench)) is not null,
            () => { goTo = box!.GoToButton.Text; box.GoToButton.InvokeCommand(Command.Accept); },
            () => Tab(workbench).File.Name == "Diff.cs");

        Assert.Equal(" Esc Close ", close);
        Assert.Equal(" Enter Go to definition ", goTo);
        Assert.Null(Box(workbench));
    }

    [Fact]
    public async Task Any_other_key_closes_it_and_acts_in_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var typed = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); },
            () => { Execute(host); },
            () => Box(workbench) is not null,
            () => host.App.InjectKey(Key.X),
            () => Box(workbench) is null,
            () => { typed = Tab(workbench).Lines[4]; Tab(workbench).MoveCursor(5, 26); },
            () => { Execute(host); },
            () => Box(workbench) is not null,
            () => host.App.InjectKey(Key.CursorRight),
            () => Box(workbench) is null);

        Assert.Equal("        var hunks = Diff.Hxunks();", typed);
        Assert.Equal(("Caller.cs", 5, 27), Where(workbench));
    }

    [Fact]
    public async Task A_long_answer_scrolls_with_the_arrows_and_page_keys_without_moving_the_caret()
    {
        _server.Hover = (word, _) => new JsonObject { ["contents"] = string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"Line {i}")) };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        QuickInfoView? box = null;
        int afterDown = -1, afterPageDown = -1, afterUp = -1;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); },
            () => { Execute(host); },
            () => (box = Box(workbench)) is not null,
            () => host.App.InjectKey(Key.CursorDown),
            () => { afterDown = box!.Top; host.App.InjectKey(Key.PageDown); },
            () => { afterPageDown = box!.Top; host.App.InjectKey(Key.CursorUp); },
            () => { afterUp = box!.Top; });

        Assert.True(box!.Frame.Height <= QuickInfo.MaxHeight);
        Assert.Equal(1, afterDown);
        Assert.True(afterPageDown > afterDown);
        Assert.Equal(afterPageDown - 1, afterUp);
        Assert.Same(box, Box(workbench));
        Assert.Equal(("Caller.cs", 4, 26), Where(workbench));
    }

    [Fact]
    public async Task Near_the_bottom_of_the_editor_it_opens_above_the_line()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, languageServers: _server);
        HostSteps.PinScreenSize(host, 80, 14);
        QuickInfoView? box = null;
        var caretY = 0;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(5, 26); },
            () => { caretY = Tab(workbench).CaretOnScreen!.Value.Y; Execute(host); },
            () => (box = Box(workbench)) is not null);

        Assert.Equal(caretY, box!.FrameToScreen().Bottom);
    }

    [Fact]
    public async Task F1_while_it_is_open_lists_its_keys_and_leaves_it_open()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        KeysDialog? help = null;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); },
            () => { Execute(host); },
            () => Box(workbench) is not null,
            () => host.App.InjectKey(Key.F1),
            () => (help = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => !workbench.SubViews.OfType<KeysDialog>().Any());

        Assert.Equal("Quick info", help!.Sheet.Here!.Heading);
        Assert.Equal(["Esc", "Enter", "↑ ↓ PgUp PgDn", "Any other key"], help.Sheet.Here.Rows().Select(r => r.Keys));
        Assert.NotNull(Box(workbench));
    }

    [Fact]
    public async Task On_whitespace_it_says_there_is_nothing_to_show()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var shown = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(3, 0); },
            () => { Execute(host); },
            () => workbench.StatusBar.Message == "Nothing to show here",
            () => { shown = workbench.StatusBar.Message; });

        Assert.Equal("Nothing to show here", shown);
        Assert.Null(Box(workbench));
    }

    [Fact]
    public async Task Only_the_latest_of_two_quick_presses_shows()
    {
        _server.HoldHover = new ManualResetEventSlim();
        _server.HoldHoverOf = "Hunks";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> rows = [], later = [];
        var waited = 0;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); },
            () => { Execute(host); },
            () => ++waited > 10,
            () => { Tab(workbench).MoveCursor(5, 30); },
            () => { Execute(host); },
            () => Box(workbench) is not null,
            () => { rows = Box(workbench)!.Rows; _server.HoldHover.Set(); },
            () => _server.Received("textDocument/hover").Count() == 2 && ++waited > 30,
            () => { later = Box(workbench)!.Rows; });

        Assert.Equal("class Widget", rows[0]);
        Assert.Equal(rows, later);
    }

    [Fact]
    public async Task While_loading_it_says_so_as_gd_does_and_the_menu_item_is_dimmed()
    {
        _server.HoldLoading = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pressed = "";
        var dimmed = false;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => workbench.StatusBar.DisplayedState.Contains("C# ◌ loading", StringComparison.Ordinal),
            () => { Execute(host); },
            () => { pressed = workbench.StatusBar.Message; host.App.InjectKey(Key.G.WithAlt); },
            () => workbench.MenuBar.IsOpen(),
            () => { dimmed = host.Menu.Items.Single(i => i.Id == CommandIds.QuickInfo).Item.Dimmed; host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen());

        Assert.Equal("The C# language server is still loading", pressed);
        Assert.True(dimmed);
        Assert.Null(Box(workbench));
    }

    [Fact]
    public async Task Without_the_server_installed_it_gives_gds_install_hint()
    {
        _server.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pressed = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => { workbench.StatusBar.ShowMessage("elsewhere"); Execute(host); },
            () => { pressed = workbench.StatusBar.Message; });

        Assert.Equal("No C# language server. Install: dotnet tool install -g csharp-ls", pressed);
    }

    [Fact]
    public async Task A_file_without_a_server_says_there_is_none()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pressed = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "README.md"); },
            () => { Execute(host); },
            () => { pressed = workbench.StatusBar.Message; });

        Assert.Equal("No language server for Markdown", pressed);
    }

    [Fact]
    public void Gi_is_on_Ctrl_G_I_in_the_editor_right_after_Go_to_definition_in_the_Go_menu()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Quick info", commands.Registered.Single(c => c.Id == CommandIds.QuickInfo).Label);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.QuickInfo));
        Assert.Equal("gi", CommandMnemonics.For(CommandIds.QuickInfo));
        Assert.Equal("Ctrl+G i", keybindings.Bindings.Single(b => b.CommandId == CommandIds.QuickInfo).Display);
        var go = host.Menu.Items.Select(i => i.Id).ToList();
        Assert.Equal(go.IndexOf(CommandIds.GoToDefinition) + 1, go.IndexOf(CommandIds.QuickInfo));
    }

    private void Add(string path, string content)
    {
        _fs.AddFile(path, new MockFileData(content));
        _server.Files[_fs.Path.GetFullPath(path)] = content;
    }

    private static bool Ready(Workbench.Workbench workbench) =>
        workbench.StatusBar.DisplayedState.Contains("C# ● ready", StringComparison.Ordinal);

    private static void Execute(WorkbenchHost host)
    {
        host.App.InjectKey(Key.G.WithCtrl);
        host.App.InjectKey(Key.I);
    }

    private static QuickInfoView? Box(Workbench.Workbench workbench) => workbench.SubViews.OfType<QuickInfoView>().SingleOrDefault();

    private static (string File, int Row, int Column) Where(Workbench.Workbench workbench) =>
        (Tab(workbench).File.Name, Tab(workbench).CursorRow, Tab(workbench).CursorColumn);

    private void OpenFile(Workbench.Workbench workbench, string name) =>
        workbench.OpenFile(_fs.FileInfo.New($"/work/{name}"));

    private static TuiCode.Editor.EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

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
        HostSteps.PinScreenSize(host, 80, 25);
        return host;
    }
}
