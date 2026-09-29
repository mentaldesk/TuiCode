using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `gb` / Ctrl+G B (#330) through the host against a fake git. Boots a TG Application — serialised (#77).
public class BlameHostTests : StaticConfigurationTest
{
    private static readonly GitBlameLine Committed = new(
        "4a91c0e2b7d1f3e5a6c8b9d0e1f2a3b4c5d6e7f8", "Nicky Baumann", new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero),
        "A clean tab shows what is actually on disk (#269)", "bravo");

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();

    public BlameHostTests()
    {
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\nbravo\ncharlie\n"));
        _git.Root = "/work";
        _git.Blame = Committed;
    }

    [Fact]
    public async Task Ctrl_G_B_shows_who_changed_the_cursor_line_and_Esc_returns_to_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        BlameView? shown = null;

        await HostSteps.Run(host,
            () => OpenFile(workbench, row: 1),
            () => host.App.InjectKey(Key.G.WithCtrl),
            () => host.App.InjectKey(Key.B),
            () => (shown = Dialog(workbench)) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null);

        Assert.Equal("Blame", shown!.Title);
        Assert.Equal("a.txt:2", shown.Location);
        Assert.StartsWith("4a91c0e  Nicky Baumann  ", shown.Commit);
        Assert.EndsWith("(2026-09-04)", shown.Commit);
        Assert.Equal("A clean tab shows what is actually on disk (#269)", shown.Subject);
        Assert.Equal("  2 │ bravo", shown.LineText);
        Assert.Equal("", shown.Status);
        Assert.Equal((2, null), Assert.Single(_git.Blames));
        Assert.True(workbench.Editor.Group.ActiveTab!.ContentHasFocus);
    }

    [Fact]
    public async Task A_dirty_tab_is_blamed_as_it_is_on_screen()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () =>
            {
                OpenFile(workbench, row: 0);
                var tab = workbench.Editor.Group.ActiveTab!;
                tab.Content = "new\nalpha\nbravo\ncharlie\n";
                tab.MoveCursor(2, 0);
            },
            () => commands.TryExecute(CommandIds.GitBlame),
            () => Dialog(workbench) is not null,
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal((3, "new\nalpha\nbravo\ncharlie\n"), Assert.Single(_git.Blames));
    }

    [Fact]
    public async Task An_uncommitted_line_shows_no_commit_and_says_so_at_the_foot()
    {
        _git.Blame = Committed with { Hash = new string('0', 40), Author = "External file (--contents)" };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        BlameView? shown = null;

        await HostSteps.Run(host,
            () => OpenFile(workbench, row: 1),
            () => commands.TryExecute(CommandIds.GitBlame),
            () => (shown = Dialog(workbench)) is not null,
            () => host.App.InjectKey(Key.Esc));

        Assert.Null(shown!.Commit);
        Assert.Null(shown.Subject);
        Assert.Equal("  2 │ bravo", shown.LineText);
        Assert.Equal(BlameView.NotCommitted, shown.Status);
    }

    [Fact]
    public async Task Clicking_the_hint_closes_the_dialog()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenFile(workbench, row: 1),
            () => commands.TryExecute(CommandIds.GitBlame),
            () => Dialog(workbench) is not null,
            () => { Dialog(workbench)!.SubViews.OfType<Button>().Single(b => b.Text == "Esc close")
                .NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonClicked, Position = new System.Drawing.Point(1, 0) }); },
            () => Dialog(workbench) is null);

        Assert.True(workbench.Editor.Group.ActiveTab!.ContentHasFocus);
    }

    [Fact]
    public async Task Focus_leaving_the_dialog_closes_it_and_the_next_blame_is_for_the_line_you_are_on_now()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        BlameView? second = null;

        await HostSteps.Run(host,
            () => OpenFile(workbench, row: 1),
            () => host.App.InjectKey(Key.G.WithCtrl),
            () => host.App.InjectKey(Key.B),
            () => Dialog(workbench) is not null,
            () =>
            {
                var tab = workbench.Editor.Group.ActiveTab!;
                tab.FocusContent();
                tab.MoveCursor(2, 0);
            },
            () => Dialog(workbench) is null,
            () => host.App.InjectKey(Key.G.WithCtrl),
            () => host.App.InjectKey(Key.B),
            () => (second = Dialog(workbench)) is not null,
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("a.txt:3", second!.Location);
        Assert.Equal([2, 3], _git.Blames.Select(b => b.Line));
    }

    [Theory]
    [InlineData("none", "No file is open.")]
    [InlineData("no repo", "a.txt isn't in a git repository.")]
    [InlineData("untracked", "a.txt isn't tracked by git.")]
    [InlineData("no git", "git isn't installed or isn't on PATH")]
    [InlineData("timeout", "git didn't answer within 5 s")]
    [InlineData("last line", "Nothing to blame on the empty last line.")]
    public async Task Blame_with_nothing_to_say_says_why_in_the_status_bar_and_opens_no_dialog(string state, string message)
    {
        if (state == "no repo") _git.Root = null;
        if (state == "untracked") _git.Blame = null;
        if (state == "no git") _git.Missing = true;
        if (state == "timeout") _git.BlameError = "git didn't answer within 5 s";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () =>
            {
                if (state != "none") OpenFile(workbench, row: state == "last line" ? 3 : 1);
                commands.TryExecute(CommandIds.GitBlame);
            },
            () => workbench.StatusBar.DisplayedText == message);

        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public void Gb_is_Git_blame_on_Ctrl_G_B_in_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands, out var keybindings);

        Assert.Equal("Git blame", commands.Registered.Single(c => c.Id == CommandIds.GitBlame).Label);
        Assert.Equal(CommandScope.Editor, commands.ScopeOf(CommandIds.GitBlame));
        Assert.Equal("gb", CommandMnemonics.For(CommandIds.GitBlame));
        var binding = Assert.Single(keybindings.Bindings, b => b.CommandId == CommandIds.GitBlame);
        Assert.Equal("Ctrl+G b", binding.Display);
    }

    [Fact]
    public async Task Blame_is_offered_with_a_file_open_and_not_without_one()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        bool? withoutFile = null, withFile = null;

        await HostSteps.Run(host,
            () => { withoutFile = commands.IsEnabled(CommandIds.GitBlame); },
            () => OpenFile(workbench, row: 0),
            () => { withFile = commands.IsEnabled(CommandIds.GitBlame); });

        Assert.False(withoutFile);
        Assert.True(withFile);
    }

    private static BlameView? Dialog(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<BlameView>().SingleOrDefault();

    private void OpenFile(Workbench.Workbench workbench, int row)
    {
        workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
        workbench.Editor.Group.ActiveTab!.MoveCursor(row, 0);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands) =>
        BuildHost(workbench, out commands, out _);

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands, out KeybindingService keybindings)
    {
        commands = new CommandService();
        keybindings = new KeybindingService(commands);
        return new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git);
    }
}
