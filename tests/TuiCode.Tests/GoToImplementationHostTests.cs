using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `gi` / Ctrl+G I (#699) through the host against a fake language server. Boots a TG Application — serialised (#77).
public class GoToImplementationHostTests : StaticConfigurationTest
{
    private const string Caller = """
        class Caller
        {
            void Go(IShape shape, IClock clock, INothing nothing)
            {
                var area = shape.Area();
                var now = clock.Now();
                nothing.Never();
            }
        }
        """;

    private static readonly string Clock = "class SystemClock : IClock\n{\n" + string.Concat(Enumerable.Repeat("    // padding\n", 40))
        + "    public int Now() => 0;\n" + string.Concat(Enumerable.Repeat("    // padding\n", 40)) + "}\n";

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _server = new();

    public GoToImplementationHostTests()
    {
        Add("/work/Caller.cs", Caller);
        Add("/work/IShape.cs", "interface IShape\n{\n    int Area();\n}\n");
        Add("/work/IClock.cs", "interface IClock\n{\n    int Now();\n}\n");
        Add("/work/INothing.cs", "interface INothing\n{\n    int Never();\n}\n");
        Add("/work/Shapes/Square.cs", "class Square : IShape\n{\n    public int Area() => 4;\n}\n");
        Add("/work/Shapes/Circle.cs", "class Circle : IShape\n{\n    public int Area() => 3;\n}\n");
        Add("/work/Time/SystemClock.cs", Clock);
        _fs.AddFile("/work/README.md", new MockFileData("# Read me"));
    }

    [Fact]
    public async Task Ctrl_G_I_on_a_call_with_one_implementation_goes_there_below_the_sticky_lines_and_Ctrl_G_P_comes_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        (string File, int Row, int Column) jumped = default, back = default;
        var topRow = 0;

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(5, 24); Execute(host); },
            () => Tab(workbench) is { File.Name: "SystemClock.cs", StickyScan.Done: true },
            () => { jumped = Where(workbench); topRow = Tab(workbench).TopRow; host.App.InjectKey(Key.G.WithCtrl); },
            () => host.App.InjectKey(Key.P),
            () => Tab(workbench).File.Name == "Caller.cs",
            () => { back = Where(workbench); });

        Assert.Equal(("SystemClock.cs", 42, 15), jumped);
        Assert.Equal(42 - 1 - 2, topRow);
        Assert.Equal(("Caller.cs", 5, 24), back);
        Assert.Equal("Caller.cs", _server.Received("textDocument/implementation").Select(p => Path.GetFileName(p!["textDocument"]!["uri"]!.GetValue<string>())).Single());
    }

    [Fact]
    public async Task The_mnemonic_gi_does_the_same()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(5, 24); host.App.InjectKey(Key.Space.WithCtrl); },
            () => host.App.InjectKey(Key.G),
            () => host.App.InjectKey(Key.I),
            () => Tab(workbench).File.Name == "SystemClock.cs");

        Assert.Equal(("SystemClock.cs", 42, 15), Where(workbench));
    }

    [Fact]
    public async Task Several_implementations_open_a_picker_named_after_the_member_and_Enter_goes_to_the_one_chosen()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> rows = [];
        DefinitionPickerView? picker = null;

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 25); Execute(host); },
            () => (picker = Picker(workbench)) is not null,
            () => { rows = picker!.VisibleItems; host.App.InjectKey(Key.CursorDown); },
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null && Tab(workbench).File.Name == "Square.cs");

        Assert.Equal("Implementations of IShape.Area", picker!.Title);
        Assert.Equal([Path.Combine("Shapes", "Circle.cs") + ":3", Path.Combine("Shapes", "Square.cs") + ":3"], rows);
        Assert.Equal(("Square.cs", 2, 15), Where(workbench));
    }

    [Fact]
    public async Task Esc_in_the_picker_leaves_the_cursor_where_it_was()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 25); Execute(host); },
            () => Picker(workbench) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => Picker(workbench) is null);

        Assert.Equal(("Caller.cs", 4, 25), Where(workbench));
    }

    [Fact]
    public async Task On_an_interface_name_it_lists_the_classes_that_implement_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        DefinitionPickerView? picker = null;
        IReadOnlyList<string> rows = [];

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(2, 14); Execute(host); },
            () => (picker = Picker(workbench)) is not null,
            () => { rows = picker!.VisibleItems; host.App.InjectKey(Key.Esc); },
            () => Picker(workbench) is null);

        Assert.Equal("Implementations of IShape", picker!.Title);
        Assert.Equal([Path.Combine("Shapes", "Circle.cs") + ":1", Path.Combine("Shapes", "Square.cs") + ":1"], rows);
    }

    [Fact]
    public async Task With_nothing_implementing_it_the_status_bar_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(6, 17); Execute(host); },
            () => workbench.StatusBar.Message == "No implementations of INothing.Never");

        Assert.Equal(("Caller.cs", 6, 17), Where(workbench));
    }

    [Theory]
    [InlineData(null, "csharp-ls can't find implementations")]
    [InlineData("Pyright", "Pyright can't find implementations")]
    public async Task A_server_that_cant_find_implementations_is_never_asked_and_says_so(string? name, string message)
    {
        _server.FindsImplementations = false;
        _server.ServerName = name;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(5, 24); Execute(host); },
            () => workbench.StatusBar.Message == message);

        Assert.Empty(_server.Received("textDocument/implementation"));
        Assert.Equal(("Caller.cs", 5, 24), Where(workbench));
    }

    [Fact]
    public async Task Without_the_server_installed_it_says_what_Ctrl_G_D_says()
    {
        _server.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string definition = "", implementation = "";

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => workbench.StatusBar.Bar.Current is not null,
            () => { workbench.StatusBar.ShowMessage("elsewhere"); host.App.InjectKey(Key.G.WithCtrl); },
            () => host.App.InjectKey(Key.D),
            () => { definition = workbench.StatusBar.DisplayedText; workbench.StatusBar.ShowMessage("elsewhere"); Execute(host); },
            () => { implementation = workbench.StatusBar.DisplayedText; });

        Assert.Equal("No C# language server. Install: dotnet tool install -g csharp-ls", definition);
        Assert.Equal(definition, implementation);
    }

    [Fact]
    public async Task While_loading_it_says_so_and_the_menu_dims_it_as_it_dims_Go_to_definition()
    {
        _server.HoldLoading = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pressed = "";
        (bool Definition, bool Implementation) whileLoading = default, whenReady = default;

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => workbench.StatusBar.DisplayedState.Contains("C# ◌ loading", StringComparison.Ordinal),
            () => Execute(host),
            () => { pressed = workbench.StatusBar.Message; host.App.InjectKey(Key.G.WithAlt); },
            () => workbench.MenuBar.IsOpen(),
            () => { whileLoading = Dimmed(host); host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen(),
            () => _server.FinishLoading(),
            () => Ready(workbench),
            () => host.App.InjectKey(Key.G.WithAlt),
            () => workbench.MenuBar.IsOpen(),
            () => { whenReady = Dimmed(host); host.App.InjectKey(Key.Esc); },
            () => !workbench.MenuBar.IsOpen());

        Assert.Equal("The C# language server is still loading", pressed);
        Assert.Equal((true, true), whileLoading);
        Assert.Equal((false, false), whenReady);
    }

    [Fact]
    public async Task F1_in_the_editor_lists_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        KeysDialog? help = null;

        await HostSteps.Run(host,
            () => OpenFile(workbench, "Caller.cs"),
            () => Ready(workbench),
            () => host.App.InjectKey(Key.F1),
            () => (help = workbench.SubViews.OfType<KeysDialog>().SingleOrDefault()) is not null,
            () => host.App.InjectKey(Key.Esc));

        var rows = help!.Sheet.Here!.Rows();
        Assert.Contains(rows, r => r.Label == "Go to implementation" && r.Keys == "Ctrl+G i");
        Assert.Contains(rows, r => r.Label == "Quick info" && r.Keys == "Ctrl+G q");
    }

    [Fact]
    public void Gi_is_on_Ctrl_G_I_in_the_editor_right_after_Go_to_definition_in_the_Go_menu()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Go to implementation", commands.Registered.Single(c => c.Id == CommandIds.GoToImplementation).Label);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.GoToImplementation));
        Assert.Equal("gi", CommandMnemonics.For(CommandIds.GoToImplementation));
        Assert.Equal("Ctrl+G i", keybindings.Bindings.Single(b => b.CommandId == CommandIds.GoToImplementation).Display);
        Assert.Single(keybindings.Bindings, b => b.Display == "Ctrl+G i");
        Assert.Single(keybindings.Bindings, b => b.Display == "Ctrl+G q");
        var go = host.Menu.Items.Select(i => i.Id).ToList();
        Assert.Equal(go.IndexOf(CommandIds.GoToDefinition) + 1, go.IndexOf(CommandIds.GoToImplementation));
        Assert.Equal('i', TuiCode.Workbench.Menus.CommandMenu.Letters[CommandIds.GoToImplementation]);
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

    private static (bool, bool) Dimmed(WorkbenchHost host) =>
        (host.Menu.Items.Single(i => i.Id == CommandIds.GoToDefinition).Item.Dimmed,
         host.Menu.Items.Single(i => i.Id == CommandIds.GoToImplementation).Item.Dimmed);

    private static (string File, int Row, int Column) Where(Workbench.Workbench workbench) =>
        (Tab(workbench).File.Name, Tab(workbench).CursorRow, Tab(workbench).CursorColumn);

    private static DefinitionPickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<DefinitionPickerView>().SingleOrDefault();

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
