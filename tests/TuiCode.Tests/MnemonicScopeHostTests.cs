using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Mnemonics;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Ctrl+Space offers Global plus the scope the keys were in when it opened (#285), so a command you
// can't run here isn't listed, isn't runnable and never captures a keystroke. Boots a TG Application —
// serialised (#77).
public class MnemonicScopeHostTests : StaticConfigurationTest
{
    private static readonly string[] Editing =
        ["mu", "md", "du", "dd", "aa", "ab", "sno", "spo", "sao", "tc", "gs", "gl", "gp", "gn", "di", "cg", "cts"];

    private static readonly string[] FileCommands = ["df", "mf", "xf", "pf"];

    private static readonly string[] DiffCommands = ["nc", "pc", "rc", "ra"];

    private static readonly string[] Globals =
        ["q", "?", "sf", "cf", "nt", "pt", "of", "os", "ts", "f1", "f9"];

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();

    public MnemonicScopeHostTests()
    {
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\n"));
    }

    public static TheoryData<string, string, string[], string[]> Leaders => new()
    {
        { CommandIds.FocusEditorBody, "Editor", Editing, [.. FileCommands, .. DiffCommands] },
        { CommandIds.FocusSidebar, "Explorer", FileCommands, [.. Editing, .. DiffCommands] },
        { CommandIds.FindGlobally, "Find", [], [.. Editing, .. FileCommands, .. DiffCommands] },
        { CommandIds.CompareToSaved, "Diff", DiffCommands, [.. Editing, .. FileCommands] },
        { CommandIds.FocusEditorTabStrip, "Tabs", Editing, [.. FileCommands, .. DiffCommands] },
        { CommandIds.FindInFile, "Find", Editing, [.. FileCommands, .. DiffCommands] },
        { CommandIds.FocusReview, "Review", [], [.. Editing, .. FileCommands, .. DiffCommands] },
    };

    [Theory]
    [MemberData(nameof(Leaders))]
    public async Task The_leader_offers_what_applies_where_it_was_opened(
        string focusCommand, string word, string[] present, string[] absent)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> listed = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\nTWO\n"; },
            () => commands.TryExecute(focusCommand),
            () => workbench.StatusBar.DisplayedFocus == word,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => { listed = Leader(workbench)!.Mnemonics; host.App.InjectKey(Key.Esc); },
            () => Leader(workbench) is null);

        Assert.All(Globals, m => Assert.Contains(m, listed));
        Assert.All(present, m => Assert.Contains(m, listed));
        Assert.All(absent, m => Assert.DoesNotContain(m, listed));
    }

    [Fact]
    public async Task With_no_file_open_the_leader_offers_none_of_the_open_file_commands()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> listed = [];

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => { listed = Leader(workbench)!.Mnemonics; host.App.InjectKey(Key.Esc); },
            () => Leader(workbench) is null);

        Assert.All(["gl", "gp", "gn", "di", "cg"], m => Assert.DoesNotContain(m, listed));
    }

    // Decided 5: an out-of-scope command has to seem not to exist. 'u' would have completed mu, which
    // the explorer can't run — so the key is ignored outright, not rejected with a message.
    [Fact]
    public async Task An_out_of_scope_second_key_does_nothing_at_all()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var stillOpen = false;
        var said = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\ntwo\n"; },
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => host.App.InjectKey(new Key('m')),
            () => Leader(workbench)!.Mnemonics.SequenceEqual(["mf"]),
            () => { said = workbench.StatusBar.DisplayedText; host.App.InjectKey(new Key('u')); },
            () => { stillOpen = Leader(workbench)?.Mnemonics.SequenceEqual(["mf"]) == true; host.App.InjectKey(Key.Esc); },
            () => Leader(workbench) is null);

        Assert.True(stillOpen, "The leader closed or re-widened after a key no in-scope mnemonic could complete");
        Assert.Equal("one\ntwo\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
        Assert.Equal(said, workbench.StatusBar.DisplayedText);
        Assert.Equal("Explorer", workbench.StatusBar.DisplayedFocus);
    }

    [Fact]
    public async Task The_editor_ignores_an_explorer_only_second_key_and_still_runs_its_own()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\ntwo\n"; },
            () => { workbench.Editor.Group.ActiveTab!.MoveCursor(1, 0); },
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => host.App.InjectKey(new Key('m')),
            () => Leader(workbench)!.Mnemonics.SequenceEqual(["md", "mu"]),
            // 'f' would have completed mf, which belongs to the explorer: ignored, prefix intact.
            () => host.App.InjectKey(new Key('f')),
            () => Leader(workbench)!.Mnemonics.SequenceEqual(["md", "mu"]),
            () => host.App.InjectKey(new Key('u')),
            () => Leader(workbench) is null);

        Assert.Equal("two\none\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
    }

    // Enter is an explicit key rather than auto-fire, so the lone visible match commits on it even
    // though typing 'm' alone never would.
    [Fact]
    public async Task Enter_still_commits_the_lone_visible_match()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => { workbench.Sidebar.Explorer.SelectedObject = Node(workbench, "a.txt"); },
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => host.App.InjectKey(new Key('m')),
            () => Leader(workbench)!.Mnemonics.SequenceEqual(["mf"]),
            () => host.App.InjectKey(Key.Enter),
            () => Prompt(workbench) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => Prompt(workbench) is null);

        Assert.True(_fs.File.Exists("/work/a.txt"));
    }

    // Opening the leader pushes a modal capture scope, so the scope has to be taken as it opens.
    [Fact]
    public async Task The_leader_keeps_the_scope_it_opened_in_when_focus_moves_under_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        IReadOnlyList<string> listed = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => workbench.Sidebar.Explorer.SetFocus(),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => { listed = Leader(workbench)!.Mnemonics; host.App.InjectKey(Key.Esc); },
            () => Leader(workbench) is null);

        Assert.Contains("mu", listed);
        Assert.DoesNotContain("mf", listed);
    }

    /// <summary>
    /// The guard that lets us narrow the list without carrying a second, unfiltered copy of the table:
    /// over the real commands, no scope's subset makes <see cref="MnemonicResolver.ResolveExact"/> fire
    /// on a prefix the full set wouldn't. It holds because the table is prefix-free
    /// (<c>CommandMnemonicsTests.No_mnemonic_is_a_prefix_of_another</c>) — if that ever stops being true,
    /// this fails and the second table is back on the table.
    /// </summary>
    [Fact]
    public async Task Narrowing_the_list_never_makes_a_mnemonic_fire_a_keystroke_earlier()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor");

        var everything = commands.Registered
            .Where(c => CommandMnemonics.For(c.Id) is not null)
            .Select(c => new MnemonicEntry(c.Id, CommandMnemonics.For(c.Id)!, c.Label))
            .ToList();
        var full = new MnemonicResolver(everything);
        var prefixes = everything
            .SelectMany(e => Enumerable.Range(1, e.Mnemonic.Length).Select(n => e.Mnemonic[..n]))
            .Distinct(StringComparer.Ordinal);

        Assert.NotEmpty(everything);
        foreach (var scope in Enum.GetValues<CommandScope>())
        {
            var narrowed = new MnemonicResolver(WorkbenchHost.MnemonicsInScope(commands, scope));
            foreach (var prefix in prefixes)
                Assert.True(narrowed.ResolveExact(prefix) is null || full.ResolveExact(prefix) is not null,
                    $"'{prefix}' fires in {scope} but not against every command — the mnemonic table is no "
                    + "longer prefix-free, so filtering the leader's list can no longer be safe on its own.");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_leader_offers_open_pull_request_only_in_a_git_repository(bool inRepo)
    {
        if (!inRepo) _fs.Directory.Delete("/work/.git");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> listed = [];

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => Leader(workbench) is not null,
            () => { listed = Leader(workbench)!.Mnemonics; host.App.InjectKey(Key.Esc); },
            () => Leader(workbench) is null);

        Assert.Equal(inRepo, listed.Contains("opr"));
    }

    private static MnemonicView? Leader(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<MnemonicView>().SingleOrDefault();

    private static PathPromptView? Prompt(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<PathPromptView>().SingleOrDefault();

    private static IFileSystemInfo Node(Workbench.Workbench workbench, string name)
    {
        var explorer = workbench.Sidebar.Explorer;
        explorer.Expand(explorer.Root!);
        return explorer.GetChildren(explorer.Root!).Single(c => c.Name == name);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        var workbench = new Workbench.Workbench(sidebar, new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
    }
}
