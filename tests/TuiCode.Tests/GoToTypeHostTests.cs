using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `gt` / Ctrl+G T (#653) through the host against fake language servers. Boots a TG Application — serialised (#77).
public class GoToTypeHostTests : StaticConfigurationTest
{
    private static readonly string Servers = "namespace TuiCode.Workbench.Languages;\n\n" + string.Concat(Enumerable.Repeat("// padding\n", 30))
        + "public sealed class LanguageServers\n{\n    void Reconcile() { }\n}\n\npublic record LanguageServerSpec(string Id);\n";

    private const string Shapes = """
        namespace TuiCode.Shapes;

        public interface IShape { }

        public struct Point { }

        public enum Colour { Red }
        """;

    private const string Caller = "class Caller\n{\n    void Go()\n    {\n    }\n}\n";

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _server = new();
    private readonly InMemorySettingsService _settings = new();

    public GoToTypeHostTests()
    {
        Add("/work/src/Languages/LanguageServers.cs", Servers);
        Add("/work/src/Shapes.cs", Shapes);
        Add("/work/Caller.cs", Caller);
        Add("/work/web/widget.ts", "export class Widget {\n    draw(): void { }\n}\n");
        _fs.AddFile("/work/README.md", new MockFileData("# Read me"));
    }

    [Fact]
    public async Task Ctrl_G_T_lists_only_types_with_their_container_and_path_and_filters_as_you_type()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        TypePickerView? picker = null;
        IReadOnlyList<string> all = [], filtered = [], labels = [];

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => Execute(host),
            () => (picker = Picker(workbench)) is { Shown.Count: > 0 },
            () => { all = picker!.VisibleItems; foreach (var c in "LSrv") host.App.InjectKey(new Key(c)); },
            () => picker!.VisibleItems.SequenceEqual(["LanguageServers", "LanguageServerSpec"]),
            () => { filtered = picker!.VisibleItems; labels = picker.Labels; });

        Assert.Equal("Go to type", picker!.Title);
        Assert.Equal(["Caller", "LanguageServers", "LanguageServerSpec", "IShape", "Point", "Colour"], all);
        Assert.Equal(["LanguageServers", "LanguageServerSpec"], filtered);
        Assert.Matches(@"^LanguageServers\s+…[\w.]*\.Workbench\.Languages\s+src/Languages/LanguageServers\.cs$", labels[0].Replace('\\', '/'));
        Assert.Equal("LSrv", _server.SymbolQueries.Last());
    }

    [Fact]
    public async Task Without_a_container_from_the_server_a_C_sharp_type_shows_its_files_namespace()
    {
        _server.NoContainerNames = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> labels = [];

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => Execute(host),
            () => Picker(workbench) is { Shown.Count: > 0 },
            () => { labels = Picker(workbench)!.Labels; });

        Assert.Matches(@"^Point\s+TuiCode\.Shapes\s+src/Shapes\.cs$", labels.Single(l => l.StartsWith("Point", StringComparison.Ordinal)).Replace('\\', '/'));
        Assert.Matches(@"^Caller\s+Caller\.cs$", labels.Single(l => l.StartsWith("Caller", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Enter_opens_the_file_at_the_declaration_and_Ctrl_G_P_goes_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        (string File, int Row, int Column) jumped = default, back = default;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(2, 4); Execute(host); },
            () => Picker(workbench) is { Shown.Count: > 0 },
            () => { foreach (var c in "LSrv") host.App.InjectKey(new Key(c)); },
            () => Picker(workbench)!.VisibleItems.SequenceEqual(["LanguageServers", "LanguageServerSpec"]),
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null && Tab(workbench).File.Name == "LanguageServers.cs",
            () => { jumped = Where(workbench); host.App.InjectKey(Key.G.WithCtrl); },
            () => host.App.InjectKey(Key.P),
            () => { back = Where(workbench); });

        Assert.Equal(("LanguageServers.cs", 32, 20), jumped);
        Assert.Equal(("Caller.cs", 2, 4), back);
    }

    [Fact]
    public async Task Esc_clears_the_filter_and_then_closes_the_picker()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var cleared = "x";
        var openAfterClear = false;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => Execute(host),
            () => Picker(workbench) is { Shown.Count: > 0 },
            () => host.App.InjectKey(new Key('P')),
            () => host.App.InjectKey(Key.Esc),
            () => { cleared = Picker(workbench)!.FilterField.Text; openAfterClear = Picker(workbench) is not null; host.App.InjectKey(Key.Esc); },
            () => Picker(workbench) is null);

        Assert.Equal("", cleared);
        Assert.True(openAfterClear);
        Assert.Equal("Caller.cs", Tab(workbench).File.Name);
    }

    [Fact]
    public async Task With_C_sharp_and_TypeScript_servers_running_both_languages_types_are_listed()
    {
        _settings.LanguageServers = new Dictionary<string, LanguageServerSetting>
        {
            ["typescript"] = new("typescript-language-server", ["--stdio"]),
        };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        IReadOnlyList<string> rows = [];

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { OpenFile(workbench, "web/widget.ts"); },
            () => workbench.Languages!.Running.Count(s => s.State == LanguageServerState.Ready) == 2,
            () => Execute(host),
            () => Picker(workbench) is { } picker && picker.VisibleItems.Contains("Widget") && picker.VisibleItems.Contains("Caller"),
            () => { rows = Picker(workbench)!.VisibleItems; });

        Assert.Contains("Widget", rows);
        Assert.Contains("LanguageServers", rows);
    }

    [Fact]
    public async Task While_the_server_loads_the_picker_says_so_and_fills_in_once_it_is_ready()
    {
        _server.HoldLoading = true;
        using var answer = new ManualResetEventSlim();
        _server.HoldWorkspaceSymbol = answer;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        TypePickerView? picker = null;
        var shownWhileLoading = -1;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => workbench.Languages!.Running.Any(),
            () => Execute(host),
            () => (picker = Picker(workbench)) is not null,
            () => Text(picker!).Contains("csharp-ls is loading the solution…", StringComparison.Ordinal),
            () => { shownWhileLoading = picker!.Shown.Count; _server.FinishLoading(); },
            () => Text(picker!).Contains("Searching…", StringComparison.Ordinal),
            () => answer.Set(),
            () => picker!.Shown.Count > 0);

        Assert.Equal(0, shownWhileLoading);
        Assert.Contains("LanguageServers", picker!.VisibleItems);
    }

    [Fact]
    public async Task With_no_server_running_the_picker_says_which_file_starts_one()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        TypePickerView? picker = null;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "README.md"); },
            () => Execute(host),
            () => (picker = Picker(workbench)) is not null && Text(picker).Contains("No language", StringComparison.Ordinal));

        Assert.Contains("No language server is running. Open a C# file to start csharp-ls", Text(picker!));
        Assert.Empty(_server.Launches);
    }

    [Fact]
    public async Task With_the_server_not_installed_the_picker_gives_the_install_command()
    {
        _server.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        TypePickerView? picker = null;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => workbench.Languages!.Running.Any(s => s.State == LanguageServerState.Missing),
            () => Execute(host),
            () => (picker = Picker(workbench)) is not null && Text(picker).Contains("Install", StringComparison.Ordinal));

        Assert.Contains(LanguageServerSpec.CSharp.NotInstalled, Text(picker!));
    }

    [Fact]
    public async Task A_server_that_fails_the_search_says_no_types_match()
    {
        _server.FailWorkspaceSymbol = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        TypePickerView? picker = null;

        await HostSteps.Run(host,
            () => { OpenFile(workbench, "Caller.cs"); },
            () => Ready(workbench),
            () => { Execute(host); host.App.InjectKey(new Key('W')); },
            () => (picker = Picker(workbench)) is not null && Text(picker).Contains(TypePickerView.NoTypes, StringComparison.Ordinal));

        Assert.Empty(picker!.Shown);
    }

    [Fact]
    public void Gt_is_on_Ctrl_G_T_everywhere_with_a_mnemonic_and_sits_after_Go_to_symbol_in_the_Go_menu()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Go to type", commands.Registered.Single(c => c.Id == CommandIds.GoToType).Label);
        Assert.Equal(CommandScope.Global, commands.ScopeOf(CommandIds.GoToType));
        Assert.Equal("gt", CommandMnemonics.For(CommandIds.GoToType));
        Assert.Equal("Ctrl+G t", keybindings.Bindings.Single(b => b.CommandId == CommandIds.GoToType).Display);
        var go = host.Menu.Items.Select(i => i.Id).ToList();
        Assert.Equal(go.IndexOf(CommandIds.GoToSymbol) + 1, go.IndexOf(CommandIds.GoToType));
        Assert.Equal("Go to t_ype", host.Menu.Items.Single(i => i.Id == CommandIds.GoToType).Item.Title);
    }

    private void Add(string path, string content)
    {
        _fs.AddFile(path, new MockFileData(content));
        _server.Files[_fs.Path.GetFullPath(path)] = content;
    }

    private static string Text(TypePickerView picker) => picker.Says;

    private static bool Ready(Workbench.Workbench workbench) =>
        workbench.StatusBar.DisplayedState.Contains("C# ● ready", StringComparison.Ordinal);

    private static void Execute(WorkbenchHost host)
    {
        host.App.InjectKey(Key.G.WithCtrl);
        host.App.InjectKey(Key.T);
    }

    private static (string File, int Row, int Column) Where(Workbench.Workbench workbench) =>
        (Tab(workbench).File.Name, Tab(workbench).CursorRow, Tab(workbench).CursorColumn);

    private static TypePickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<TypePickerView>().SingleOrDefault();

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
            _settings, driverName: DriverRegistry.Names.ANSI, languageServers: _server);
        HostSteps.PinScreenSize(host, 100, 30);
        return host;
    }
}
