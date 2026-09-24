using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Throwing a file's changes away in one go (`ra`) against a diff's left side (#248).
public class DiffRevertAllTests
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public void Revert_all_takes_the_buffer_back_to_the_left_side()
    {
        using var group = new EditorGroup();
        var (tab, diff) = Compare(group, "one\ntwo\nthree\nfour", "ONE\ntwo\nstray\nthree\nFOUR");

        Assert.Equal(3, diff.RevertAll());
        Assert.Equal(["one", "two", "three", "four"], tab.Lines);
    }

    [Fact]
    public void Nothing_is_left_to_revert_afterwards()
    {
        using var group = new EditorGroup();
        var (_, diff) = Compare(group, "one\ntwo\nthree", "ONE\ntwo\nTHREE");
        diff.RevertAll();

        Assert.Equal(0, diff.ChangeCount);
        Assert.Null(diff.RevertAll());
    }

    [Fact]
    public void Revert_all_leaves_the_file_on_disk_alone()
    {
        using var group = new EditorGroup();
        var (tab, diff) = Compare(group, "one\ntwo", "ONE\nTWO");
        diff.RevertAll();

        Assert.True(tab.IsDirty);
        Assert.Equal("one\ntwo", _fs.File.ReadAllText("/work/a.txt"));
    }

    private (EditorTab Tab, DiffTab Diff) Compare(EditorGroup group, string saved, string buffer)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        var tab = group.OpenOrFocus(_fs.FileInfo.New("/work/a.txt"));
        tab.Content = buffer;
        return (tab, group.CompareToSaved(tab)!);
    }
}

// Drives `ra` through the host. Boots a TG Application — serialised (#77).
public class RevertAllHostTests : StaticConfigurationTest
{
    // What's on disk: the diff's left side, and what a revert takes the buffer back to.
    private const string Saved = "line 1\nline 2\nline 3\nline 4\nline 5\nline 6";

    // Three separate change blocks, at rows 1, 3 and 5.
    private const string Edited = "line 1\nLINE 2\nline 3\nLINE 4\nline 5\nLINE 6";

    private readonly MockFileSystem _fs = new();

    [Fact]
    public void Revert_all_is_a_diff_scoped_command_with_no_default_key()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

        Assert.Equal("Revert all changes in file", commands.Registered.Single(c => c.Id == CommandIds.RevertAllChanges).Label);
        Assert.Equal(CommandScope.Diff, commands.ScopeOf(CommandIds.RevertAllChanges));
        Assert.Equal("ra", CommandMnemonics.For(CommandIds.RevertAllChanges));
        Assert.DoesNotContain(keybindings.Bindings, b => b.CommandId == CommandIds.RevertAllChanges);
    }

    [Fact]
    public async Task Confirming_reverts_every_change_and_writes_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var asked = "";

        await HostSteps.Run(host,
            () => OpenThreeChanges(workbench, commands),
            () => commands.TryExecute(CommandIds.RevertAllChanges),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); host.App.InjectKey(Key.Tab); },
            () => Confirm(workbench)!.ConfirmHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Equal("Revert all 3 changes in a.txt?", asked);
        Assert.Equal(Saved.Split('\n'), workbench.Editor.Group.Tabs[0].Lines);
        Assert.Equal(0, workbench.Editor.Group.ActiveDiffTab!.ChangeCount);
        Assert.True(workbench.Editor.Group.Tabs[0].IsDirty);
        Assert.Equal(Saved, _fs.File.ReadAllText("/work/a.txt"));
        Assert.StartsWith("Reverted 3 changes in a.txt — Ctrl+Z to undo", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task One_change_is_reported_in_the_singular()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var asked = "";

        await HostSteps.Run(host,
            () => OpenDiff(workbench, commands, "alpha\nbravo", "alpha\nBRAVO"),
            () => commands.TryExecute(CommandIds.RevertAllChanges),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); host.App.InjectKey(Key.Tab); },
            () => Confirm(workbench)!.ConfirmHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Equal("Revert all 1 change in a.txt?", asked);
        Assert.StartsWith("Reverted 1 change in a.txt — Ctrl+Z to undo", workbench.StatusBar.DisplayedText);
    }

    [Theory]
    [InlineData(false)] // Enter lands on the focused Cancel
    [InlineData(true)]
    public async Task Cancelling_leaves_the_buffer_as_it_was(bool escape)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenThreeChanges(workbench, commands),
            () => commands.TryExecute(CommandIds.RevertAllChanges),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(escape ? Key.Esc : Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Equal(Edited.Split('\n'), workbench.Editor.Group.Tabs[0].Lines);
        Assert.Equal(3, workbench.Editor.Group.ActiveDiffTab!.ChangeCount);
        Assert.DoesNotContain("Reverted", workbench.StatusBar.DisplayedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_ctrl_z_in_the_editor_takes_the_whole_revert_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => OpenThreeChanges(workbench, commands),
            () => commands.TryExecute(CommandIds.RevertAllChanges),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(Key.Tab),
            () => Confirm(workbench)!.ConfirmHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveTab is not null,
            () => host.App.InjectKey(Key.Z.WithCtrl));

        Assert.Equal(Edited.Split('\n'), group.Tabs[0].Lines);
    }

    [Fact]
    public async Task Ra_on_a_diff_with_no_changes_says_so_and_opens_no_dialog()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenDiff(workbench, commands, "alpha\nbravo", "alpha\nBRAVO"),
            () => host.App.InjectKey(Key.R.WithCtrl),
            () => commands.TryExecute(CommandIds.RevertAllChanges));

        Assert.Null(Confirm(workbench));
        Assert.StartsWith("No changes", workbench.StatusBar.DisplayedText);
    }

    private void OpenThreeChanges(Workbench.Workbench workbench, CommandService commands) =>
        OpenDiff(workbench, commands, Saved, Edited);

    private void OpenDiff(Workbench.Workbench workbench, CommandService commands, string saved, string buffer)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
        workbench.Editor.Group.ActiveTab!.Content = buffer;
        commands.TryExecute(CommandIds.CompareToSaved);
    }

    private static ConfirmView? Confirm(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ConfirmView>().SingleOrDefault();

    private static string Message(ConfirmView view) => view.SubViews.OfType<Label>().First().Text;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
