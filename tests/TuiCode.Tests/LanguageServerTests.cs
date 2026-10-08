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
        using var server = Server();
        server.Start();
        server.Open(Widget, "class A { }");
        server.Change(Widget, "class B { }");

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

    private LanguageServer Server(TimeSpan? quietLoad = null) =>
        new(LanguageServerSpec.CSharp, Root, _fake, action => action()) { QuietLoad = quietLoad ?? TimeSpan.FromMinutes(1) };

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
