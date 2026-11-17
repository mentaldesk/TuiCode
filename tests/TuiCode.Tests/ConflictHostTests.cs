using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// The merge conflict commands and status through the workbench (#693). Boots a TG Application — serialised (#77).
public class ConflictHostTests : StaticConfigurationTest
{
    private static readonly string[] Keeps = [CommandIds.KeepCurrentChange, CommandIds.KeepIncomingChange, CommandIds.KeepBothChanges];
    private static readonly string[] Steps = [CommandIds.NextMergeConflict, CommandIds.PreviousMergeConflict];

    private readonly MockFileSystem _fs = new();

    public ConflictHostTests()
    {
        _fs.AddFile("/work/a.cs", new MockFileData(string.Join('\n',
            "var app = builder.Build();", "<<<<<<< HEAD", "mine", "=======", "theirs", ">>>>>>> origin/main", "app.Run();")));
        _fs.AddFile("/work/flat.txt", new MockFileData("one\ntwo\n"));
    }

    [Fact]
    public void The_five_are_editor_commands_in_the_Edit_menu_with_their_mnemonics_and_no_keys()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out var keybindings);
        string[] ids = [.. Keeps, .. Steps];

        Assert.All(ids, id => Assert.Equal(CommandScope.Editor, commands.Registered.Single(c => c.Id == id).Scope));
        Assert.Equal(["Keep current change", "Keep incoming change", "Keep both changes", "Next merge conflict", "Previous merge conflict"],
            ids.Select(id => commands.Registered.Single(c => c.Id == id).Label));
        Assert.Equal(["kcc", "kic", "kbc", "nmc", "pmc"], ids.Select(CommandMnemonics.For));
        Assert.Subset(CommandMenu.Layout.Single(m => m.Title == "_Edit").Ids.ToHashSet(), ids.ToHashSet());
        Assert.DoesNotContain(keybindings.Bindings, binding => ids.Contains(binding.CommandId));
    }

    [Fact]
    public async Task Keep_commands_run_only_in_a_conflict_and_next_and_previous_only_in_a_file_with_one()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out _);
        EditorTab? tab = null;
        bool[] outside = [], inside = [], flat = [];

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs")); tab.FocusContent(); },
            () =>
            {
                outside = [.. Keeps.Concat(Steps).Select(commands.IsEnabled)];
                tab!.MoveCursor(4, 0);
                inside = [.. Keeps.Concat(Steps).Select(commands.IsEnabled)];
                workbench.Editor.Open(_fs.FileInfo.New("/work/flat.txt"));
                flat = [.. Keeps.Concat(Steps).Select(commands.IsEnabled)];
                return true;
            });

        Assert.Equal([false, false, false, true, true], outside);
        Assert.Equal([true, true, true, true, true], inside);
        Assert.Equal([false, false, false, false, false], flat);
    }

    [Fact]
    public async Task The_status_bar_counts_the_conflicts_and_says_when_the_last_one_is_gone()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out _);
        EditorTab? tab = null;
        string? before = null, inside = null;

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs")); tab.FocusContent(); },
            () => { before = workbench.StatusBar.DisplayedConflicts; tab!.MoveCursor(2, 0); return true; },
            () => { inside = workbench.StatusBar.DisplayedConflicts; return commands.TryExecute(CommandIds.KeepIncomingChange); },
            () => workbench.StatusBar.DisplayedConflicts is null);

        Assert.Equal("1 conflict", before);
        Assert.Equal("Conflict 1 of 1", inside);
        Assert.Equal(["var app = builder.Build();", "theirs", "app.Run();"], tab!.Lines);
        Assert.Equal("No conflicts left in a.cs", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task A_file_with_no_conflicts_shows_nothing_new()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _, out _);

        await HostSteps.Run(host,
            () => { workbench.Editor.Open(_fs.FileInfo.New("/work/flat.txt")).FocusContent(); },
            () => workbench.StatusBar.DisplayedFocus == "Editor");

        Assert.Null(workbench.StatusBar.DisplayedConflicts);
        Assert.DoesNotContain("onflict", workbench.StatusBar.DisplayedState);
    }

    [Fact]
    public async Task Next_and_previous_at_the_ends_stay_put_and_say_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out _);
        EditorTab? tab = null;
        string? last = null;

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs")); tab.FocusContent(); },
            () => commands.TryExecute(CommandIds.NextMergeConflict),
            () => { Assert.Equal(1, tab!.CursorRow); return commands.TryExecute(CommandIds.NextMergeConflict); },
            () => { last = workbench.StatusBar.Message; return commands.TryExecute(CommandIds.PreviousMergeConflict); });

        Assert.Equal("Last conflict in file", last);
        Assert.Equal("First conflict in file", workbench.StatusBar.Message);
        Assert.Equal(1, tab!.CursorRow);
    }

    [Fact]
    public async Task Keeping_a_side_and_saving_leaves_the_file_unmerged()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("a.cs", "one\n", "First");
        repo.Git("checkout", "-q", "-b", "feature");
        repo.Commit("a.cs", "mine\n", "Feature");
        repo.Git("checkout", "-q", "main");
        repo.Commit("a.cs", "theirs\n", "Main");
        repo.Git("checkout", "-q", "feature");
        repo.TryGit("merge", "main");
        var disk = new FileSystem();
        using var workbench = BuildWorkbench(disk.DirectoryInfo.New(repo.Path));
        using var host = BuildHost(workbench, out var commands, out _);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(disk.FileInfo.New(repo.File("a.cs"))); tab.FocusContent(); tab.MoveCursor(1, 0); },
            () => commands.TryExecute(CommandIds.KeepCurrentChange),
            () => commands.TryExecute(CommandIds.SaveActiveEditor),
            () => !tab!.IsDirty);

        Assert.Equal("mine\n", File.ReadAllText(repo.File("a.cs")));
        Assert.Equal("UU a.cs", repo.Git("status", "--porcelain=v1"));
    }

    private Workbench.Workbench BuildWorkbench(IDirectoryInfo? root = null)
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()),
            new EditorPart(new SyntaxHighlighter(GrammarBundle.Load())), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(root ?? _fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands, out KeybindingService keybindings)
    {
        commands = new CommandService();
        keybindings = new KeybindingService(commands);
        return new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
