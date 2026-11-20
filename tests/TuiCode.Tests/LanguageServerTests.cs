using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class LanguageServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly string Root = Path.GetFullPath("/work");
    private static readonly string Widget = Path.GetFullPath("/work/Widget.cs");

    private readonly FakeLanguageServer _fake = new();

    [Fact]
    public async Task It_loads_until_the_server_says_it_has_finished_then_is_ready()
    {
        _fake.HoldLoading = true;
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);

        server.Start();
        await Until(() => _fake.Methods.Contains("initialized"));
        Assert.Equal(LanguageServerState.Loading, server.State);

        _fake.FinishLoading();
        await ready.WaitAsync(Timeout);
        Assert.Equal(("csharp-ls", Root), _fake.Launches.Single());
    }

    [Fact]
    public async Task A_server_that_finishes_loading_before_it_answers_initialize_is_ready_once_it_has()
    {
        _fake.ReportsProgress = false;
        _fake.LoadsWhileInitializing = true;
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);

        server.Start();

        await ready.WaitAsync(Timeout);
    }

    [Fact]
    public async Task A_server_that_reports_no_progress_is_ready_once_it_has_been_quiet()
    {
        _fake.ReportsProgress = false;
        using var server = Server(TimeSpan.FromMilliseconds(50));
        var ready = Reached(server, LanguageServerState.Ready);

        server.Start();

        await ready.WaitAsync(Timeout);
    }

    [Fact]
    public void A_command_that_isnt_installed_is_missing()
    {
        _fake.Missing = true;
        using var server = Server();

        server.Start();

        Assert.Equal(LanguageServerState.Missing, server.State);
    }

    [Fact]
    public async Task Open_edit_save_and_close_reach_the_server_with_rising_versions()
    {
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Open(Widget, "class Widget { }");
        server.Start();
        await ready.WaitAsync(Timeout);

        server.Change(Widget, "class Widget { int Count; }");
        server.Change(Widget, "class Widget { int Total; }");
        server.Save(Widget, "class Widget { int Total; }");
        server.Close(Widget);
        await Until(() => _fake.Methods.Contains("textDocument/didClose"));

        var opened = _fake.Received("textDocument/didOpen").Single()!["textDocument"]!;
        Assert.Equal("csharp", opened["languageId"]!.GetValue<string>());
        Assert.Equal(1, opened["version"]!.GetValue<int>());
        Assert.Equal("class Widget { }", opened["text"]!.GetValue<string>());
        Assert.Equal([2, 3], _fake.Received("textDocument/didChange").Select(p => p!["textDocument"]!["version"]!.GetValue<int>()));
        Assert.Equal("class Widget { int Total; }", _fake.Received("textDocument/didSave").Single()!["text"]!.GetValue<string>());
        Assert.Empty(_fake.Documents);
        Assert.Equal(
            ["initialize", "initialized", "textDocument/didOpen", "textDocument/didChange", "textDocument/didChange", "textDocument/didSave", "textDocument/didClose"],
            _fake.Methods);
    }

    [Fact]
    public async Task Edits_made_before_the_server_has_started_open_it_at_their_version()
    {
        _fake.HoldLoading = true;
        using var initializing = new ManualResetEventSlim();
        _fake.HoldInitialize = initializing;
        using var server = Server();
        server.Start();
        server.Open(Widget, "class A { }");
        server.Change(Widget, "class B { }");
        initializing.Set();

        await Until(() => _fake.Methods.Contains("textDocument/didOpen"));

        var opened = _fake.Received("textDocument/didOpen").Single()!["textDocument"]!;
        Assert.Equal(2, opened["version"]!.GetValue<int>());
        Assert.Equal("class B { }", opened["text"]!.GetValue<string>());
        Assert.DoesNotContain("textDocument/didChange", _fake.Methods);
    }

    [Fact]
    public async Task Definition_asks_at_the_position_and_reads_back_the_places()
    {
        _fake.Files[Path.GetFullPath("/work/Gadget.cs")] = "class Gadget\n{\n    void Run() { }\n}";
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Open(Widget, "class Widget { void Go() { Run(); } }");
        server.Start();
        await ready.WaitAsync(Timeout);

        var found = await server.DefinitionAsync(Widget, 0, 28);

        Assert.Equal([new SourceLocation(Path.GetFullPath("/work/Gadget.cs"), 2, 9)], found);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Implementation_asks_at_the_position_and_reads_back_locations_or_links(bool links)
    {
        _fake.ImplementationLinks = links;
        _fake.Files[Path.GetFullPath("/work/IGadget.cs")] = "interface IGadget\n{\n    void Run();\n}";
        _fake.Files[Path.GetFullPath("/work/Gadget.cs")] = "class Gadget : IGadget\n{\n    public void Run() { }\n}";
        _fake.Files[Path.GetFullPath("/work/Gizmo.cs")] = "class Gizmo : IGadget\n{\n    public void Run() => Spin();\n}";
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Open(Widget, "class Widget { void Go(IGadget g) { g.Run(); } }");
        server.Start();
        await ready.WaitAsync(Timeout);

        var found = await server.ImplementationAsync(Widget, 0, 39);

        Assert.True(server.FindsImplementations);
        Assert.Equal([new SourceLocation(Path.GetFullPath("/work/Gadget.cs"), 2, 16), new SourceLocation(Path.GetFullPath("/work/Gizmo.cs"), 2, 16)], found);
        var capability = _fake.Received("initialize").Single()!["capabilities"]!["textDocument"]!["implementation"]!;
        Assert.True(capability["linkSupport"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_server_that_doesnt_offer_implementations_is_never_asked_for_them()
    {
        _fake.FindsImplementations = false;
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Open(Widget, "class Widget { void Go(IGadget g) { g.Run(); } }");
        server.Start();
        await ready.WaitAsync(Timeout);

        var found = await server.ImplementationAsync(Widget, 0, 39);

        Assert.False(server.FindsImplementations);
        Assert.Empty(found);
        Assert.DoesNotContain("textDocument/implementation", _fake.Methods);
    }

    [Theory]
    [InlineData("csharp-ls", null, "csharp-ls")]
    [InlineData("csharp-ls", "csharp-ls 0.16", "csharp-ls")]
    [InlineData("my-lsp", null, "my-lsp")]
    [InlineData("my-lsp", "My LSP", "My LSP")]
    public async Task Its_title_is_the_name_TuiCode_knows_it_by_else_the_name_it_gives(string command, string? given, string title)
    {
        _fake.ServerName = given;
        using var server = new LanguageServer(LanguageServerSpec.CSharp with { Command = command }, Root, _fake, action => action())
        {
            QuietLoad = TimeSpan.FromMinutes(1),
            StopTimeout = Timeout,
        };
        var ready = Reached(server, LanguageServerState.Ready);
        server.Start();
        await ready.WaitAsync(Timeout);

        Assert.Equal(title, server.Title);
    }

    [Fact]
    public void Pyright_is_called_Pyright_in_a_sentence() =>
        Assert.Equal("Pyright", new LanguageServerSpec("python", "Python", "pyright-langserver", ["--stdio"], null).Title);

    [Fact]
    public async Task It_tells_the_server_it_can_rename_with_prepareRename_and_document_changes()
    {
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Start();
        await ready.WaitAsync(Timeout);

        var capabilities = _fake.Received("initialize").Single()!["capabilities"]!;

        Assert.True(capabilities["textDocument"]!["rename"]!["prepareSupport"]!.GetValue<bool>());
        Assert.True(capabilities["workspace"]!["workspaceEdit"]!["documentChanges"]!.GetValue<bool>());
        Assert.Null(capabilities["workspace"]!["workspaceEdit"]!["resourceOperations"]);
    }

    [Fact]
    public async Task Rename_asks_prepareRename_first_where_the_server_offers_it_then_reads_back_the_edits()
    {
        _fake.Files[Path.GetFullPath("/work/Gadget.cs")] = "class Gadget\n{\n    void Run() { }\n}";
        using var server = await Started("class Widget { void Go() { Run(); } }");

        var target = await server.PrepareRenameAsync(Widget, 0, 28);
        var edits = await server.RenameAsync(Widget, 0, 28, "Start");

        Assert.True(server.CanRename);
        Assert.Equal(new RenameTarget(new TextEdit(0, 27, 0, 30, ""), "Run"), target);
        Assert.Equal(
            [(Path.GetFullPath("/work/Gadget.cs"), new TextEdit(2, 9, 2, 12, "Start")), (Widget, new TextEdit(0, 27, 0, 30, "Start"))],
            edits.SelectMany(f => f.Edits.Select(e => (f.Path, e))));
        var rename = _fake.Received("textDocument/rename").Single()!;
        Assert.Equal("Start", rename["newName"]!.GetValue<string>());
        Assert.Equal(28, rename["position"]!["character"]!.GetValue<int>());
    }

    [Fact]
    public async Task Without_prepareProvider_it_doesnt_ask_prepareRename()
    {
        _fake.RenameProvider = true;
        using var server = await Started("class Widget { void Go() { Run(); } }");

        var target = await server.PrepareRenameAsync(Widget, 0, 28);

        Assert.True(server.CanRename);
        Assert.Equal(new RenameTarget(null, null), target);
        Assert.Empty(_fake.Received("textDocument/prepareRename"));
    }

    [Fact]
    public async Task A_server_without_renameProvider_cant_rename()
    {
        _fake.RenameProvider = null;
        using var server = await Started("class Widget { }");

        Assert.False(server.CanRename);
    }

    [Fact]
    public async Task Rename_reads_documentChanges_and_leaves_out_the_file_operations()
    {
        _fake.RenamesAsDocumentChanges = true;
        using var server = await Started("class Widget { void Run() { Run(); } }");

        var edits = await server.RenameAsync(Widget, 0, 21, "Start");

        var file = Assert.Single(edits);
        Assert.Equal(Widget, file.Path);
        Assert.Equal([new TextEdit(0, 20, 0, 23, "Start"), new TextEdit(0, 28, 0, 31, "Start")], file.Edits);
    }

    [Fact]
    public async Task A_rename_the_server_refuses_throws_in_its_words()
    {
        _fake.RenameError = "Start is already defined";
        using var server = await Started("class Widget { void Run() { } }");

        var refused = await Assert.ThrowsAsync<JsonRpcException>(() => server.RenameAsync(Widget, 0, 21, "Start"));

        Assert.Equal("Start is already defined", refused.Message);
    }

    [Fact]
    public async Task A_place_prepareRename_says_no_to_has_no_target()
    {
        using var server = await Started("class Widget { void Run() { } }");

        Assert.Null(await server.PrepareRenameAsync(Widget, 0, 16));
    }

    [Fact]
    public async Task It_tells_the_server_it_can_show_decompiled_library_sources()
    {
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Start();
        await ready.WaitAsync(Timeout);

        var initialize = _fake.Received("initialize").Single()!;

        Assert.True(initialize["capabilities"]!["experimental"]!["csharp"]!["metadataUris"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_decompiled_file_is_asked_about_by_the_servers_own_uri_and_never_opened()
    {
        const string uri = "csharp:/work/App.csproj/decompiled/System.Console.cs";
        var file = Path.GetFullPath("/tmp/TuiCode/decompiled/System.Console.cs");
        _fake.Library[uri] = new DecompiledSource("System.Console", "System.Console", "class Console\n{\n    void Beep() { }\n    void Go() { Beep(); }\n}");
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Start();
        await ready.WaitAsync(Timeout);

        var source = await server.DecompiledAsync(uri);
        server.Alias(file, uri);
        server.Open(file, source!.Source);
        var found = await server.DefinitionAsync(file, 3, 17);

        Assert.Equal(("System.Console", "System.Console"), (source.AssemblyName, source.SymbolName));
        Assert.Equal([new SourceLocation(uri, 2, 9)], found);
        Assert.DoesNotContain("textDocument/didOpen", _fake.Methods);
    }

    [Fact]
    public async Task A_type_the_server_cant_decompile_has_no_source()
    {
        const string uri = "csharp:/work/App.csproj/decompiled/System.Secret.cs";
        _fake.Library[uri] = new DecompiledSource("System.Secret", "System.Secret", "class Secret { }");
        _fake.Undecompilable.Add(uri);
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Start();
        await ready.WaitAsync(Timeout);

        Assert.Null(await server.DecompiledAsync(uri));
    }

    [Fact]
    public async Task A_server_that_crashes_stops_and_isnt_started_again()
    {
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Start();
        await ready.WaitAsync(Timeout);
        var stopped = Reached(server, LanguageServerState.Stopped);

        _fake.Crash();

        await stopped.WaitAsync(Timeout);
        Assert.Single(_fake.Launches);
        Assert.True(_fake.Current.Killed);
        Assert.Empty(await server.DefinitionAsync(Widget, 0, 0));
    }

    [Fact]
    public async Task A_server_that_fails_to_initialize_stops_and_says_why()
    {
        _fake.InitializeError = "No .NET SDKs were found.";
        using var server = Server();
        var stopped = Reached(server, LanguageServerState.Stopped);

        server.Start();

        await stopped.WaitAsync(Timeout);
        Assert.Equal("No .NET SDKs were found.", server.Failure);
    }

    [Fact]
    public async Task Stopping_asks_the_server_to_shut_down_and_exit()
    {
        var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Start();
        await ready.WaitAsync(Timeout);

        await server.StopAsync().WaitAsync(Timeout);

        Assert.Equal(["shutdown", "exit"], _fake.Methods.TakeLast(2));
        Assert.True(_fake.Current.Exited.IsCompleted);
        Assert.Equal(LanguageServerState.Ready, server.State);
    }

    [Fact]
    public async Task Problems_for_an_open_file_are_reported_and_for_a_file_not_open_ignored()
    {
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        var reported = new System.Collections.Concurrent.ConcurrentQueue<FileProblems>();
        server.ProblemsReported += (_, problems) => reported.Enqueue(problems);
        server.Open(Widget, "class Widget { nam }");
        server.Start();
        await ready.WaitAsync(Timeout);

        _fake.Publish(Path.GetFullPath("/work/Other.cs"), Error());
        _fake.Publish(Widget, Error());
        await Until(() => !reported.IsEmpty);

        var only = Assert.Single(reported);
        Assert.Equal(Widget, only.Path);
        Assert.Equal(new TuiCode.Editor.Problem(0, 15, 0, 18, TuiCode.Editor.ProblemSeverity.Error, "CS0103", "nam"), only.Problems.Single());
    }

    [Fact]
    public async Task It_tells_the_server_it_takes_problems()
    {
        using var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Start();
        await ready.WaitAsync(Timeout);

        Assert.NotNull(_fake.Received("initialize").Single()!["capabilities"]!["textDocument"]!["publishDiagnostics"]);
    }

    internal static System.Text.Json.Nodes.JsonObject Error() => new()
    {
        ["range"] = new System.Text.Json.Nodes.JsonObject
        {
            ["start"] = new System.Text.Json.Nodes.JsonObject { ["line"] = 0, ["character"] = 15 },
            ["end"] = new System.Text.Json.Nodes.JsonObject { ["line"] = 0, ["character"] = 18 },
        },
        ["severity"] = 1,
        ["code"] = "CS0103",
        ["message"] = "nam",
    };

    private async Task<LanguageServer> Started(string widget)
    {
        var server = Server();
        var ready = Reached(server, LanguageServerState.Ready);
        server.Open(Widget, widget);
        server.Start();
        await ready.WaitAsync(Timeout);
        return server;
    }

    private LanguageServer Server(TimeSpan? quietLoad = null) =>
        new(LanguageServerSpec.CSharp, Root, _fake, action => action()) { QuietLoad = quietLoad ?? TimeSpan.FromMinutes(1), StopTimeout = Timeout };

    private static Task Reached(LanguageServer server, LanguageServerState state)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.StateChanged += (_, _) =>
        {
            if (server.State == state) reached.TrySetResult();
        };
        return reached.Task;
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
