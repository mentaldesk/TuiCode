using System.Text.Json.Nodes;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Go to definition into a library's decompiled source (#610), through the host against a fake csharp-ls. Boots a TG Application — serialised (#77).
public class DecompiledSourceHostTests : StaticConfigurationTest
{
    private const string Program = """
        class Program
        {
            void Main()
            {
                Console.WriteLine(1);
                Secret.Keep();
            }
        }
        """;

    private const string ConsoleUri = "csharp:/work/App.csproj/decompiled/System.Console.cs";
    private const string SecretUri = "csharp:/work/App.csproj/decompiled/System.Secret.cs";

    private const string ConsoleSource = """
        namespace System;

        /// <summary>Represents the standard input, output, and error streams.</summary>
        public static class Console
        {
            /// <summary>Writes a value, then a line terminator.</summary>
            public static void WriteLine(string value) { }

            public static void WriteLine(int value) { }
        }
        """;

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _server = new();
    private readonly CommandService _commands = new();
    private readonly WorkspaceStateStore _store;

    public DecompiledSourceHostTests()
    {
        _store = new WorkspaceStateStore(_fs, "/state.json");
        _fs.AddFile("/work/Program.cs", new MockFileData(Program));
        _server.Files[_fs.Path.GetFullPath("/work/Program.cs")] = Program;
        _server.Library[ConsoleUri] = new DecompiledSource("System.Console", "System.Console", ConsoleSource);
        _server.Library[SecretUri] = new DecompiledSource("System.Secret", "System.Secret", "public class Secret { }");
        _server.Undecompilable.Add(SecretUri);
    }

    [Fact]
    public async Task Gd_on_a_library_type_opens_its_decompiled_source_read_only_on_its_declaration_and_gp_goes_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        (string Title, int Row, int Column, string? Grammar, bool ReadOnly) opened = default;
        string resting = "", source = "";
        (string File, int Row, int Column) back = default;

        await HostSteps.Run(host,
            () => OpenProgram(workbench),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 10); Execute(host, Key.D); },
            () => Tab(workbench).ReadOnly,
            () =>
            {
                var tab = Tab(workbench);
                opened = (tab.Title, tab.CursorRow, tab.CursorColumn, tab.Grammar?.Id, tab.ReadOnly);
                source = tab.Content.ReplaceLineEndings("\n");
                resting = workbench.StatusBar.DisplayedText;
                Execute(host, Key.P);
            },
            () => Tab(workbench).File.Name == "Program.cs",
            () => { back = Where(workbench); });

        Assert.Equal(("Console.cs [decompiled]", 3, 20, "csharp", true), opened);
        Assert.Equal(ConsoleSource, source);
        Assert.Equal("Decompiled from System.Console, read-only", resting);
        Assert.Equal(("Program.cs", 4, 10), back);
        Assert.DoesNotContain(_server.Received("textDocument/didOpen"), open => open!["textDocument"]!["uri"]!.GetValue<string>().EndsWith("System.Console.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Overloads_are_listed_as_decompiled_places_and_the_one_chosen_opens_in_the_tab_already_open()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        DefinitionPickerView? picker = null;
        IReadOnlyList<string> rows = [];

        await HostSteps.Run(host,
            () => OpenProgram(workbench),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 10); Execute(host, Key.D); },
            () => Tab(workbench).ReadOnly,
            () => { workbench.OpenFile(_fs.FileInfo.New("/work/Program.cs")); },
            () => { Tab(workbench).MoveCursor(4, 18); Execute(host, Key.D); },
            () => (picker = Picker(workbench)) is not null,
            () => { rows = picker!.VisibleItems; host.App.InjectKey(Key.CursorDown); },
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null && Tab(workbench).ReadOnly);

        Assert.Equal(["System.Console [decompiled]:7", "System.Console [decompiled]:9"], rows);
        Assert.Equal(("System.Console.cs", 8, 23), Where(workbench));
        Assert.Equal(2, workbench.Editor.Groups.Tabs.Count);
    }

    [Fact]
    public async Task The_overload_quick_info_names_is_the_one_gd_lands_on_in_the_tab_already_open()
    {
        _server.Hover = (word, _) => word == "WriteLine"
            ? new JsonObject { ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = "```csharp\nvoid Console.WriteLine(int value)\n```" } }
            : null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenProgram(workbench),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 10); Execute(host, Key.D); },
            () => Tab(workbench).ReadOnly,
            () => { workbench.OpenFile(_fs.FileInfo.New("/work/Program.cs")); },
            () => { Tab(workbench).MoveCursor(4, 18); Execute(host, Key.D); },
            () => Tab(workbench).ReadOnly);

        Assert.Equal(("System.Console.cs", 8, 23), Where(workbench));
        Assert.Null(Picker(workbench));
        Assert.Equal(2, workbench.Editor.Groups.Tabs.Count);
    }

    [Fact]
    public async Task Gd_inside_the_decompiled_source_asks_the_server_about_it_by_its_own_uri()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenProgram(workbench),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 10); Execute(host, Key.D); },
            () => Tab(workbench).ReadOnly,
            () => { Tab(workbench).MoveCursor(3, 22); Execute(host, Key.D); },
            () => _server.Received("textDocument/definition").Count() == 2);

        var asked = _server.Received("textDocument/definition").Last()!["textDocument"]!["uri"]!.GetValue<string>();
        Assert.Equal(ConsoleUri, asked);
    }

    [Fact]
    public async Task A_type_the_server_cant_decompile_has_no_definition()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenProgram(workbench),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(5, 10); Execute(host, Key.D); },
            () => workbench.StatusBar.DisplayedText == "No definition found for Secret");

        Assert.Equal(("Program.cs", 5, 10), Where(workbench));
    }

    [Fact]
    public async Task Typing_pasting_cutting_undo_and_save_change_nothing_and_say_its_read_only()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var said = new List<string>();
        string copied = "";
        Key[] edits =
        [
            Key.X, Key.Enter, Key.Backspace, Key.Delete, Key.Tab, Key.V.WithCtrl, Key.X.WithCtrl, Key.K.WithCtrl,
            Key.Backspace.WithCtrl, Key.Z.WithCtrl, Key.S.WithCtrl, Key.CursorDown.WithAlt,
        ];

        var steps = new List<Delegate>
        {
            () => { OpenProgram(workbench); return true; },
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 10); Execute(host, Key.D); return true; },
            () => Tab(workbench).ReadOnly,
            () => { Tab(workbench).MoveCursor(3, 20); host.App.InjectKey(Key.C.WithCtrl); return true; },
            () => { copied = workbench.StatusBar.DisplayedText; return true; },
        };
        foreach (var key in edits)
        {
            steps.Add(() => { workbench.StatusBar.ShowMessage("before"); host.App.InjectKey(key); return true; });
            steps.Add(() => { said.Add(workbench.StatusBar.DisplayedText); return true; });
        }
        steps.Add(() => { Tab(workbench).Content = "replaced"; return true; });
        await HostSteps.Run(host, [.. steps]);

        var tab = Tab(workbench);
        Assert.Equal(ConsoleSource, tab.Content.ReplaceLineEndings("\n"));
        Assert.Equal(ConsoleSource, _fs.File.ReadAllText(tab.File.FullName));
        Assert.False(tab.IsDirty);
        Assert.Equal("Console.cs [decompiled]", tab.Title);
        Assert.StartsWith("Copied 1 line", copied, StringComparison.Ordinal);
        Assert.Equal(edits.Select(_ => DecompiledFiles.Refusal), said);
        Assert.Equal(edits.Length, said.Count);
    }

    [Fact]
    public async Task Decompiled_tabs_are_left_out_of_the_tabs_the_folder_reopens()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenProgram(workbench),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 10); Execute(host, Key.D); },
            () => Tab(workbench).ReadOnly);

        var saved = _store.Load(_fs.Path.GetFullPath("/work"))!;
        Assert.Equal([_fs.Path.GetFullPath("/work/Program.cs")], saved.Files);
        Assert.Null(saved.ActiveFile);
    }

    [Fact]
    public async Task The_roslyn_servers_decompiled_file_opens_read_only_too()
    {
        var file = _fs.Path.Combine(_fs.Path.GetTempPath(), "MetadataAsSource", "a28", "DecompilationMetadataAsSourceFileProvider", "69e", "Console.cs");
        var text = "#region Assembly System.Console, Version=10.0.0.0, Culture=neutral\n#endregion\n\nnamespace System;\n\npublic static class Console\n{\n}\n";
        _fs.AddFile(file, new MockFileData(text));
        _server.Library.Clear();
        _server.Files[file] = text;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        (string Title, int Row, string Resting) opened = default;

        await HostSteps.Run(host,
            () => OpenProgram(workbench),
            () => Ready(workbench),
            () => { Tab(workbench).MoveCursor(4, 10); Execute(host, Key.D); },
            () => Tab(workbench).ReadOnly,
            () => { opened = (Tab(workbench).Title, Tab(workbench).CursorRow, workbench.StatusBar.DisplayedText); });

        Assert.Equal(("Console.cs [decompiled]", 5, "Decompiled from System.Console, read-only"), opened);
    }

    private void OpenProgram(Workbench.Workbench workbench) => workbench.OpenFile(_fs.FileInfo.New("/work/Program.cs"));

    private static bool Ready(Workbench.Workbench workbench) =>
        workbench.StatusBar.DisplayedState.Contains("C# ● ready", StringComparison.Ordinal);

    private static void Execute(WorkbenchHost host, Key key)
    {
        host.App.InjectKey(Key.G.WithCtrl);
        host.App.InjectKey(key);
    }

    private static (string File, int Row, int Column) Where(Workbench.Workbench workbench) =>
        (Tab(workbench).File.Name, Tab(workbench).CursorRow, Tab(workbench).CursorColumn);

    private static DefinitionPickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<DefinitionPickerView>().SingleOrDefault();

    private static TuiCode.Editor.EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart(), _store);
        workbench.OpenFolder(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var host = new WorkbenchHost(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, fileSystem: _fs, languageServers: _server);
        HostSteps.PinScreenSize(host, 100, 30);
        return host;
    }
}
