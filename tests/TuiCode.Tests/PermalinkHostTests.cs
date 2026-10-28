using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `cp` and `og` (#623) through the host against a fake git. Boots a TG Application — serialised (#77).
public class PermalinkHostTests : StaticConfigurationTest
{
    private const string Head = "4a91c0e2b7d1f3e5a6c8b9d0e1f2a3b4c5d6e7f8";
    private const string Older = "1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e";
    private const string Saved = "alpha\nbravo\ncharlie\n";

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeBrowser _browser = new();
    private readonly FakeEnvironment _environment = new();
    private readonly TestClipboard _clipboard = new();

    public PermalinkHostTests()
    {
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData(Saved));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.RemoteUrl = "git@github.com:o/r.git";
        _git.Pushed["HEAD"] = new GitPushedCommit(Head, IsRevision: true);
        _git.RepoFiles[$"{Head}:a.txt"] = Saved;
    }

    [Fact]
    public async Task Copy_permalink_copies_the_cursor_line_s_link_and_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => Open(workbench, host, row: 1),
            () => commands.TryExecute(CommandIds.CopyPermalink),
            () => _clipboard.Text.Length > 0);

        Assert.Equal($"https://github.com/o/r/blob/{Head}/a.txt#L2", _clipboard.Text);
        Assert.Equal("Copied permalink to line 2", workbench.StatusBar.Message);
        Assert.Empty(_browser.Opened);
    }

    [Fact]
    public async Task A_selection_links_to_its_lines()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => Open(workbench, host, row: 0),
            () => host.App.InjectKey(Key.CursorDown.WithShift),
            () => host.App.InjectKey(Key.CursorDown.WithShift),
            () => host.App.InjectKey(Key.CursorRight.WithShift),
            () => commands.TryExecute(CommandIds.CopyPermalink),
            () => _clipboard.Text.Length > 0);

        Assert.EndsWith("#L1-L3", _clipboard.Text);
        Assert.Equal("Copied permalink to lines 1–3", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task Open_on_GitHub_opens_the_link_in_the_browser()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => Open(workbench, host, row: 2),
            () => commands.TryExecute(CommandIds.OpenOnGitHub),
            () => _browser.Opened.Count == 1);

        Assert.Equal([$"https://github.com/o/r/blob/{Head}/a.txt#L3"], _browser.Opened);
        Assert.Equal("Opened permalink to line 3", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task Over_ssh_Open_on_GitHub_copies_the_link_instead()
    {
        _environment.Set("SSH_CONNECTION", "10.0.0.1 50000 10.0.0.2 22");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => Open(workbench, host, row: 2),
            () => commands.TryExecute(CommandIds.OpenOnGitHub),
            () => _clipboard.Text.Length > 0);

        Assert.Empty(_browser.Opened);
        Assert.EndsWith("#L3", _clipboard.Text);
        Assert.Equal("Copied permalink to line 3 (no browser over SSH)", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task With_nothing_to_link_to_nothing_is_copied_and_the_status_bar_says_why()
    {
        _git.RemoteUrl = "https://gitlab.com/o/r.git";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => Open(workbench, host, row: 0),
            () => commands.TryExecute(CommandIds.CopyPermalink),
            () => workbench.StatusBar.Message.EndsWith("GitHub remote.", StringComparison.Ordinal));

        Assert.Equal("", _clipboard.Text);
        Assert.Equal("https://gitlab.com/o/r.git isn't a GitHub remote.", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task On_a_compare_to_saved_diff_a_left_only_row_links_to_the_saved_line_and_others_to_the_buffer_s()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        string leftOnly = "", right = "";

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, host, row: 0);
                workbench.Editor.Group.ActiveTab!.Content = "alpha\ncharlie\n";
            },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.Editor.Group.ActiveDiffTab is not null,
            () => workbench.Editor.Group.ActiveDiffTab!.NextChange(),
            () => commands.TryExecute(CommandIds.CopyPermalink),
            () => _clipboard.Text.Length > 0,
            () => { leftOnly = _clipboard.Text; _clipboard.Text = ""; },
            () => host.App.InjectKey(Key.CursorDown),
            () => workbench.Editor.Group.ActiveDiffTab!.CurrentDiffRow == 2,
            () => commands.TryExecute(CommandIds.CopyPermalink),
            () => _clipboard.Text.Length > 0,
            () => { right = _clipboard.Text; });

        Assert.EndsWith("#L2", leftOnly);
        Assert.EndsWith("#L3", right);
        Assert.Equal("Copied permalink to line 3 at 4a91c0e", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task A_diff_side_showing_a_pushed_commit_links_to_that_commit()
    {
        _git.Pushed[Older] = new GitPushedCommit(Older, IsRevision: true);
        _git.RepoFiles[$"{Older}:old.txt"] = "alpha\nBRAVO\ncharlie\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () =>
            {
                host.App.Driver!.Clipboard = _clipboard;
                var file = _fs.FileInfo.New("/work/a.txt");
                var diff = workbench.Editor.Group.CompareRevisions(file, "1d2e3f4^", DiffTab.SplitLines(Saved), "1d2e3f4", DiffTab.SplitLines("alpha\nBRAVO\ncharlie\n"));
                diff.RightOrigin = new DiffSideOrigin(file.FullName, Older, "old.txt");
                diff.NextChange();
            },
            () => commands.TryExecute(CommandIds.CopyPermalink),
            () => _clipboard.Text.Length > 0);

        Assert.Equal($"https://github.com/o/r/blob/{Older}/old.txt#L2", _clipboard.Text);
        Assert.Equal("Copied permalink to line 2", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task Both_are_offered_with_a_file_or_diff_open_and_not_without()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        bool? without = null, with = null;

        await HostSteps.Run(host,
            () => { without = commands.IsEnabled(CommandIds.CopyPermalink) || commands.IsEnabled(CommandIds.OpenOnGitHub); },
            () => Open(workbench, host, row: 0),
            () => { with = commands.IsEnabled(CommandIds.CopyPermalink) && commands.IsEnabled(CommandIds.OpenOnGitHub); });

        Assert.False(without);
        Assert.True(with);
    }

    [Fact]
    public void The_Selection_menu_ends_with_blame_and_the_two_links_and_the_Diff_menu_has_no_blame()
    {
        var selection = CommandMenu.Layout.Single(m => m.Title == "_Selection").Ids;
        var diff = CommandMenu.Layout.Single(m => m.Title == "_Diff").Ids;

        Assert.Equal([CommandMenu.Separator, CommandIds.GitBlame, CommandIds.CopyPermalink, CommandIds.OpenOnGitHub], selection[^4..]);
        Assert.DoesNotContain(CommandIds.GitBlame, diff);
        Assert.Equal(('g', 'm', 'h'), (CommandMenu.Letters[CommandIds.GitBlame], CommandMenu.Letters[CommandIds.CopyPermalink], CommandMenu.Letters[CommandIds.OpenOnGitHub]));
    }

    [Fact]
    public void The_mnemonics_are_cp_and_og_and_neither_has_a_default_key()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _, out var keybindings);

        Assert.Equal(("cp", "og", "gb"), (CommandMnemonics.For(CommandIds.CopyPermalink), CommandMnemonics.For(CommandIds.OpenOnGitHub), CommandMnemonics.For(CommandIds.GitBlame)));
        Assert.DoesNotContain(keybindings.Bindings, b => b.CommandId is CommandIds.CopyPermalink or CommandIds.OpenOnGitHub);
    }

    [Fact]
    public async Task The_leader_copies_with_cp()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host,
            () => Open(workbench, host, row: 0),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.C),
            () => host.App.InjectKey(Key.P),
            () => _clipboard.Text.Length > 0);

        Assert.EndsWith("#L1", _clipboard.Text);
    }

    [Fact]
    public void Bound_in_settings_they_show_in_F1_under_Selection()
    {
        var settings = new InMemorySettingsService();
        settings.SetKeybindingOverrides([
            new KeybindingOverride(TestKeys.Chord("F6"), CommandIds.CopyPermalink),
            new KeybindingOverride(TestKeys.Chord("F7"), CommandIds.OpenOnGitHub),
        ]);
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(), settings,
            driverName: DriverRegistry.Names.ANSI, git: _git);

        var everywhere = HelpSheet.Everywhere(commands, keybindings, CommandMenu.Layout, _ => true);

        var selection = Assert.Single(everywhere.Groups, g => g.Heading == "Selection");
        Assert.Contains(selection.Rows, r => r.Keys == "F6" && r.Label == "Copy permalink");
        Assert.Contains(selection.Rows, r => r.Keys == "F7" && r.Label == "Open on GitHub");
    }

    private void Open(Workbench.Workbench workbench, WorkbenchHost host, int row)
    {
        host.App.Driver!.Clipboard = _clipboard;
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
        return new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(), new InMemorySettingsService(),
            environment: _environment, driverName: DriverRegistry.Names.ANSI, git: _git) { Browser = _browser };
    }

    private sealed class FakeBrowser : IBrowser
    {
        public List<string> Opened { get; } = [];

        public string? Open(string url)
        {
            Opened.Add(url);
            return null;
        }
    }
}
