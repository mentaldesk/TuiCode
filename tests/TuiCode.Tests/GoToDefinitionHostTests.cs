using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `gd` / Ctrl+G D (#454) through the host against a fake language server. Boots a TG Application — serialised (#77).
public class GoToDefinitionHostTests : StaticConfigurationTest
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

    private static readonly string Diff = "class Diff\n{\n" + string.Concat(Enumerable.Repeat("    // padding\n", 40)) + "    int Hunks() => 0;\n" + string.Concat(Enumerable.Repeat("    // padding\n", 40)) + "}\n";

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _server = new();

    public GoToDefinitionHostTests()
    {
        Add("/work/Caller.cs", Caller);
        Add("/work/Diff.cs", Diff);
        Add("/work/Widget.cs", "partial class Widget\n{\n}\n");
        Add("/work/Widget.Draw.cs", "using System;\n\npartial class Widget\n{\n}\n");
        _fs.AddFile("/work/README.md", new MockFileData("# Read me"));
    }

    [Fact]
    public async Task Ctrl_G_D_opens_the_file_a_call_is_defined_in_with_the_caret_on_its_name_and_gp_gn_go_back_and_forth()
    {
        using var workbench = BuildWorkbench();
        var host = BuildHost(workbench);
        var languageShown = "";
        (string File, int Row, int Column) jumped = default, back = default, forward = default;
        var topRow = 0;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { languageShown = workbench.StatusBar.DisplayedText; Tab(workbench).MoveCursor(4, 26); },
            () => host.App.InjectKey(Key.G.WithCtrl),
            () => host.App.InjectKey(Key.D),
            () => Tab(workbench).File.Name == "Diff.cs",
            () => { jumped = Where(workbench); topRow = Tab(workbench).TopRow; host.App.InjectKey(Key.G.WithCtrl); },
            () => host.App.InjectKey(Key.P),
            () => { back = Where(workbench); host.App.InjectKey(Key.G.WithCtrl); },
            () => host.App.InjectKey(Key.N),
            () => { forward = Where(workbench); });
        host.Dispose();

        Assert.EndsWith("C# ● ready", languageShown, StringComparison.Ordinal);
        Assert.Equal(("Diff.cs", 42, 8), jumped);
        Assert.Equal(42 - 2, topRow);
        Assert.Equal(("Caller.cs", 4, 26), back);
        Assert.Equal(jumped, forward);
        Assert.True(_server.Current.Exited.IsCompleted);
        Assert.Contains("shutdown", _server.Methods);
    }

    [Fact]
    public async Task A_local_jumps_within_the_file()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(6, 20); },
            () => { Execute(host); },
            () => Tab(workbench).CursorRow == 4);

        Assert.Equal(("Caller.cs", 4, 12), Where(workbench));
    }

    [Fact]
    public async Task A_partial_class_lists_its_declarations_and_Enter_jumps_to_the_one_chosen()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> rows = [];
        DefinitionPickerView? picker = null;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(5, 26); },
            () => { Execute(host); },
            () => (picker = Picker(workbench)) is not null,
            () => { rows = picker!.VisibleItems; host.App.InjectKey(Key.CursorDown); },
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal("Definitions of Widget", picker!.Title);
        Assert.Equal(["Widget.Draw.cs:3", "Widget.cs:1"], rows);
        Assert.Equal(("Widget.cs", 0, 14), Where(workbench));
    }

    [Fact]
    public async Task Unsaved_edits_that_moved_the_definition_are_sent_before_asking()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Diff.cs"); },
            () => { Tab(workbench).Content = "// moved\n// down\n" + Diff; },
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 26); },
            () => { Execute(host); },
            () => Tab(workbench).File.Name == "Diff.cs");

        Assert.Equal(("Diff.cs", 44, 8), Where(workbench));
        Assert.True(Tab(workbench).IsDirty);
    }

    [Theory]
    [InlineData(3, 0, "Nothing to go to here")]
    [InlineData(0, 2, "No definition found for class")]
    public async Task With_nothing_to_jump_to_it_says_so_and_stays_put(int row, int column, string message)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var shown = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(row, column); },
            () => { Execute(host); },
            () => workbench.StatusBar.Message == message,
            () => { shown = workbench.StatusBar.Message; });

        Assert.Equal(message, shown);
        Assert.Equal(("Caller.cs", row, column), Where(workbench));
    }

    [Fact]
    public async Task Without_the_server_installed_opening_a_file_and_Ctrl_G_D_say_how_to_install_it()
    {
        _server.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string opened = "", pressed = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => { opened = workbench.StatusBar.DisplayedText; workbench.StatusBar.SetMessage("elsewhere"); },
            () => { Execute(host); },
            () => { pressed = workbench.StatusBar.DisplayedText; });

        Assert.Equal("No C# language server. Install: dotnet tool install -g csharp-ls  •  C#", opened);
        Assert.Equal(opened, pressed);
        Assert.Equal(("Caller.cs", 0, 0), Where(workbench));
    }

    [Fact]
    public async Task While_loading_the_status_bar_says_so_Ctrl_G_D_waits_and_the_menu_item_is_dimmed()
    {
        _server.HoldLoading = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string loading = "", pressed = "", ready = "";
        bool dimmedWhileLoading = false, dimmedWhenReady = true;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => workbench.StatusBar.DisplayedText.EndsWith("C# ◌ loading", StringComparison.Ordinal),
            () => { loading = workbench.StatusBar.DisplayedText; Execute(host); },
            () => { pressed = workbench.StatusBar.Message; host.App.InjectKey(Key.G.WithAlt); },
            () => workbench.MenuBar.IsOpen(),
            () => { dimmedWhileLoading = Item(host).Dimmed; host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen(),
            () => { _server.FinishLoading(); },
            () => Ready(workbench),
            () => { ready = workbench.StatusBar.DisplayedText; host.App.InjectKey(Key.G.WithAlt); },
            () => workbench.MenuBar.IsOpen(),
            () => { dimmedWhenReady = Item(host).Dimmed; host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen());

        Assert.EndsWith("C# ◌ loading", loading, StringComparison.Ordinal);
        Assert.Equal("The C# language server is still loading", pressed);
        Assert.True(dimmedWhileLoading);
        Assert.EndsWith("C# ● ready", ready, StringComparison.Ordinal);
        Assert.False(dimmedWhenReady);
    }

    [Fact]
    public async Task A_server_that_crashes_shows_as_stopped_and_isnt_restarted()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var shown = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { _server.Crash(); },
            () => workbench.StatusBar.DisplayedText.EndsWith("C# stopped", StringComparison.Ordinal),
            () => { OpenFile(workbench, "Diff.cs"); },
            () => { shown = workbench.StatusBar.DisplayedText; });

        Assert.EndsWith("C# stopped", shown, StringComparison.Ordinal);
        Assert.Single(_server.Launches);
    }

    [Fact]
    public async Task A_file_without_a_server_shows_just_its_language_and_Ctrl_G_D_says_there_is_none()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string opened = "", pressed = "";

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "README.md"); },
            () => { opened = workbench.StatusBar.DisplayedText; Execute(host); },
            () => { pressed = workbench.StatusBar.Message; });

        Assert.EndsWith("  •  Markdown  •  Wrap", opened, StringComparison.Ordinal);
        Assert.Equal("No language server for Markdown", pressed);
        Assert.Empty(_server.Launches);
    }

    [Fact]
    public void Gd_is_on_Ctrl_G_D_in_the_editor_with_a_mnemonic_and_a_place_in_the_Go_menu()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Go to definition", commands.Registered.Single(c => c.Id == CommandIds.GoToDefinition).Label);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.GoToDefinition));
        Assert.Equal("gd", CommandMnemonics.For(CommandIds.GoToDefinition));
        Assert.Equal("Ctrl+G d", keybindings.Bindings.Single(b => b.CommandId == CommandIds.GoToDefinition).Display);
        var go = host.Menu.Items.Select(i => i.Id).ToList();
        Assert.Equal(go.IndexOf(CommandIds.GoToSymbol) + 1, go.IndexOf(CommandIds.GoToDefinition));
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
        host.App.InjectKey(Key.D);
    }

    private static (string File, int Row, int Column) Where(Workbench.Workbench workbench) =>
        (Tab(workbench).File.Name, Tab(workbench).CursorRow, Tab(workbench).CursorColumn);

    private static DefinitionPickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<DefinitionPickerView>().SingleOrDefault();

    private static TuiCode.Workbench.Menus.CommandMenuItem Item(WorkbenchHost host) =>
        host.Menu.Items.Single(i => i.Id == CommandIds.GoToDefinition).Item;

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
