using TuiCode.Editor;
using TuiCode.Syntax;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class LanguageServersTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _fake = new();
    private readonly List<Action> _scheduled = [];

    public LanguageServersTests()
    {
        _fs.AddFile("/work/Widget.cs", new MockFileData("class Widget { }"));
        _fs.AddFile("/work/Gadget.cs", new MockFileData("class Gadget { }"));
        _fs.AddFile("/work/README.md", new MockFileData("# Widgets"));
        _fs.AddFile("/other/Thing.cs", new MockFileData("class Thing { }"));
    }

    [Fact]
    public async Task The_first_CSharp_file_starts_one_server_in_the_folder_for_every_CSharp_file()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        languages.OpenFolder(Full("/work"));

        Open(group, "/work/README.md");
        Assert.Empty(_fake.Launches);
        var widget = Open(group, "/work/Widget.cs");
        Open(group, "/work/Gadget.cs");
        await Until(() => _fake.Documents.Count == 2);

        Assert.Equal(("csharp-ls", Full("/work")), _fake.Launches.Single());
        Assert.Equal("class Widget { }", _fake.Documents[Full("/work/Widget.cs")]);
        Assert.Same(languages.ServerFor(widget), languages.Running.Single());
        Assert.Null(languages.ServerFor(group.Tabs.Single(t => t.File.Name == "README.md")));
    }

    [Fact]
    public void Without_a_folder_the_server_starts_in_the_files_own()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);

        Open(group, "/other/Thing.cs");

        Assert.Equal(Full("/other"), _fake.Launches.Single().Directory);
    }

    [Fact]
    public async Task Edits_reach_the_server_once_typing_pauses_or_a_request_needs_them()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        var tab = Open(group, "/work/Widget.cs");
        await Until(() => _fake.Documents.ContainsKey(Full("/work/Widget.cs")));

        tab.Content = "class Widget { int A; }";
        tab.Content = "class Widget { int B; }";
        Assert.Single(_scheduled);
        _scheduled.Single()();
        await Until(() => _fake.Documents[Full("/work/Widget.cs")] == "class Widget { int B; }");

        tab.Content = "class Widget { int C; }";
        languages.Flush();
        await Until(() => _fake.Documents[Full("/work/Widget.cs")] == "class Widget { int C; }");
        Assert.Equal(2, _fake.Received("textDocument/didChange").Count());
    }

    [Fact]
    public async Task Closing_a_tab_or_taking_its_grammar_away_closes_the_document()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        var widget = Open(group, "/work/Widget.cs");
        var gadget = Open(group, "/work/Gadget.cs");
        await Until(() => _fake.Documents.Count == 2);

        group.CloseEditor(widget);
        gadget.SetGrammar(null);

        await Until(() => _fake.Documents.IsEmpty);
        Assert.Null(languages.ServerFor(gadget));
    }

    [Fact]
    public async Task Opening_another_folder_stops_the_server_and_the_next_file_starts_one_there()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        languages.OpenFolder(Full("/work"));
        Open(group, "/work/Widget.cs");
        await Until(() => languages.Running.Single().State == LanguageServerState.Ready);
        var first = _fake.Current;

        languages.OpenFolder(Full("/other"));
        group.CloseAll();
        Open(group, "/other/Thing.cs");

        await first.Exited.WaitAsync(Timeout);
        Assert.Contains("shutdown", _fake.Methods);
        Assert.Equal([Full("/work"), Full("/other")], _fake.Launches.Select(l => l.Directory));
    }

    [Fact]
    public async Task A_server_that_crashed_isnt_restarted_by_the_next_file()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        Open(group, "/work/Widget.cs");
        await Until(() => languages.Running.Single().State == LanguageServerState.Ready);

        _fake.Crash();
        await Until(() => languages.Running.Single().State == LanguageServerState.Stopped);
        var gadget = Open(group, "/work/Gadget.cs");

        Assert.Single(_fake.Launches);
        Assert.Equal(LanguageServerState.Stopped, languages.ServerFor(gadget)!.State);
    }

    [Fact]
    public async Task Disposing_stops_every_server()
    {
        using var group = new EditorGroup(Syntax);
        var languages = Languages(group);
        Open(group, "/work/Widget.cs");
        await Until(() => languages.Running.Single().State == LanguageServerState.Ready);

        languages.Dispose();

        Assert.True(_fake.Current.Exited.IsCompleted);
        Assert.Equal(["textDocument/didClose", "shutdown", "exit"], _fake.Methods.TakeLast(3));
    }

    private LanguageServers Languages(EditorGroup group) =>
        new(group, _fake, action => action(), (_, action) => _scheduled.Add(action)) { QuietLoad = TimeSpan.FromMinutes(1), StopTimeout = Timeout };

    private EditorTab Open(EditorGroup group, string path) => group.OpenOrFocus(_fs.FileInfo.New(path));

    private string Full(string path) => _fs.Path.GetFullPath(path);

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
