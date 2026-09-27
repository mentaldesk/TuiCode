using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// The range a jump out of a diff reveals (#287).
public class DiffChangeLinesTests
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public void A_multi_line_change_reports_every_line_of_it()
    {
        var diff = Compare(Lines(10), Changed(10, 3, 4, 5));

        diff.NextChange();

        Assert.Equal((3, 5), diff.CurrentChangeLines);
    }

    [Fact]
    public void A_freshly_opened_diff_sits_above_the_first_change_and_reports_one_line()
    {
        var diff = Compare(Lines(10), Changed(10, 3, 4, 5));

        Assert.Equal(0, diff.CurrentChange);
        Assert.Equal((0, 0), diff.CurrentChangeLines);
    }

    [Fact]
    public void A_row_below_a_change_but_above_the_next_reports_one_line()
    {
        var diff = Compare(Lines(10), Changed(10, 3, 4, 5));
        diff.NextChange();

        diff.InvokeCommand(Command.Down);
        diff.InvokeCommand(Command.Down);
        diff.InvokeCommand(Command.Down);

        Assert.Equal(1, diff.CurrentChange);
        Assert.Equal((6, 6), diff.CurrentChangeLines);
    }

    [Fact]
    public void A_change_the_buffer_has_no_lines_in_reports_the_line_below_it()
    {
        var saved = Lines(6);
        var diff = Compare(saved, string.Join('\n', saved.Split('\n').Where(line => line is not ("line 3" or "line 4"))));

        diff.NextChange();

        Assert.Equal((2, 2), diff.CurrentChangeLines);
    }

    private static string Lines(int count) => string.Join('\n', Enumerable.Range(1, count).Select(i => $"line {i}"));

    private static string Changed(int count, params int[] rows) =>
        string.Join('\n', Enumerable.Range(1, count).Select(i => rows.Contains(i - 1) ? $"LINE {i}" : $"line {i}"));

    private DiffTab Compare(string saved, string buffer)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        using var group = new EditorGroup();
        var tab = group.OpenOrFocus(_fs.FileInfo.New("/work/a.txt"));
        tab.Content = buffer;
        return group.CompareToSaved(tab)!;
    }
}

// Drives `Enter` in a diff through the host. Boots a TG Application — serialised (#77).
public class RevealJumpHostTests : StaticConfigurationTest
{
    private const int FileLines = 200;
    private const int ChangeStart = 99;
    private const int ChangeEnd = 102;

    private readonly MockFileSystem _fs = new();

    [Fact]
    public async Task Enter_on_a_multi_line_change_leaves_the_whole_block_on_screen()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            Size(host),
            () => OpenDiff(workbench, commands),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.Enter),
            () => { tab = workbench.Editor.Group.ActiveTab; });

        Assert.NotNull(tab);
        Assert.Equal(ChangeStart, tab.CursorRow);
        Assert.Equal(ChangeStart - Reveal.Margin, tab.TopRow);
        Assert.True(ChangeEnd <= tab.TopRow + tab.VisibleRows - 1, "the last line of the change is off screen");
    }

    [Fact]
    public async Task Enter_on_a_change_already_in_view_does_not_scroll()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var tops = new List<int>();
        void Record() => tops.Add(workbench.Editor.Group.ActiveTab!.TopRow);

        await HostSteps.Run(host,
            Size(host),
            () => OpenDiff(workbench, commands),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.Enter),
            Record,
            // Back to the diff it came from, on the same change, and in again.
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => host.App.InjectKey(Key.Enter),
            Record);

        Assert.Equal([ChangeStart - Reveal.Margin, ChangeStart - Reveal.Margin], tops);
    }

    [Fact]
    public async Task Enter_on_a_row_outside_a_change_reveals_that_one_line()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            Size(host),
            () => OpenDiff(workbench, commands),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.Enter),
            () => { tab = workbench.Editor.Group.ActiveTab; });

        Assert.NotNull(tab);
        Assert.Equal(ChangeEnd + 1, tab.CursorRow);
        Assert.Equal(ChangeEnd + 1 - Reveal.Margin, tab.TopRow);
    }

    // A fixed screen, so the rows the reveal works with don't depend on the runner's terminal.
    private static Action Size(WorkbenchHost host) => () => host.App.Driver!.SetScreenSize(100, 30);

    private void OpenDiff(Workbench.Workbench workbench, CommandService commands)
    {
        var saved = Enumerable.Range(1, FileLines).Select(i => $"line {i}").ToArray();
        _fs.AddFile("/work/a.txt", new MockFileData(string.Join('\n', saved)));
        workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));

        var buffer = saved.ToArray();
        for (var row = ChangeStart; row <= ChangeEnd; row++)
            buffer[row] = buffer[row].ToUpperInvariant();
        workbench.Editor.Group.ActiveTab!.Content = string.Join('\n', buffer);
        commands.TryExecute(CommandIds.CompareToSaved);
    }

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
