using TuiCode.Abstractions;
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
        _fs.AddFile("/work/main.go", new MockFileData("package main"));
        _fs.AddFile("/work/notes.txt", new MockFileData("package notes"));
    }

    [Fact]
    public async Task A_language_with_a_chosen_server_starts_it_and_a_language_without_one_starts_none()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        languages.Configure(Chosen(("go", new LanguageServerSetting("gopls", ["serve"]))));

        var go = Open(group, "/work/main.go");
        await Until(() => _fake.Documents.ContainsKey(Full("/work/main.go")));

        Assert.Equal("gopls", _fake.Launches.Single().Command);
        Assert.Equal("Go", languages.ServerFor(go)!.Spec.Name);
        Assert.Null(languages.ServerFor(Open(group, "/work/README.md")));
    }

    [Fact]
    public void Choosing_no_server_for_CSharp_starts_none()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        languages.Configure(Chosen(("csharp", LanguageServerSetting.None)));

        var widget = Open(group, "/work/Widget.cs");

        Assert.Empty(_fake.Launches);
        Assert.Null(languages.SpecFor(widget));
    }

    [Fact]
    public async Task Changing_a_languages_server_restarts_only_that_one_with_its_open_files()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        languages.Configure(Chosen(("go", new LanguageServerSetting("gopls", []))));
        Open(group, "/work/Widget.cs");
        Open(group, "/work/main.go");
        await Until(() => _fake.Documents.Count == 2);
        var csharp = _fake.Servers[0];
        var gopls = _fake.Servers[1];

        languages.Configure(Chosen(("go", new LanguageServerSetting("/opt/gopls", ["-remote=auto"]))));

        await gopls.Exited.WaitAsync(Timeout);
        Assert.False(csharp.Exited.IsCompleted);
        Assert.Equal(["csharp-ls", "gopls", "/opt/gopls"], _fake.Launches.Select(l => l.Command));
        await Until(() => _fake.Received("textDocument/didOpen").Count() == 3);
    }

    [Fact]
    public void Saving_the_same_servers_again_restarts_nothing()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        Open(group, "/work/Widget.cs");

        languages.Configure(Chosen(("csharp", new LanguageServerSetting("csharp-ls", []))));

        Assert.Single(_fake.Launches);
    }

    [Fact]
    public async Task A_file_whose_grammar_was_changed_uses_that_languages_server()
    {
        using var group = new EditorGroup(Syntax);
        using var languages = Languages(group);
        languages.Configure(Chosen(("go", new LanguageServerSetting("gopls", []))));
        var notes = Open(group, "/work/notes.txt");
        Assert.Empty(_fake.Launches);

        notes.SetGrammar(Syntax.LanguageById("go"));

        await Until(() => _fake.Documents.ContainsKey(Full("/work/notes.txt")));
        Assert.Equal("gopls", _fake.Launches.Single().Command);
    }

    private static Dictionary<string, LanguageServerSetting> Chosen(params (string Id, LanguageServerSetting Setting)[] chosen) =>
        chosen.ToDictionary(c => c.Id, c => c.Setting, StringComparer.OrdinalIgnoreCase);

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
        new(new EditorGroups(group, new EditorGroup()), _fake, action => action(), (_, action) => _scheduled.Add(action)) { QuietLoad = TimeSpan.FromMinutes(1), StopTimeout = Timeout };

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
