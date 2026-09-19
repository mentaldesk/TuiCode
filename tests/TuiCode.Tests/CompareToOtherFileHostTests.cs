using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `cto` through the host. Boots a TG Application — serialised (#77).
public class CompareToOtherFileHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    public CompareToOtherFileHostTests()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("alpha\nbravo\n"));
        _fs.AddFile("/work/src/b.txt", new MockFileData("alpha\nBRAVO\n"));
        _fs.AddFile("/work/other/b.txt", new MockFileData("zulu\n"));
        _fs.AddFile("/work/readme.md", new MockFileData(""));
    }

    [Fact]
    public async Task Cto_opens_the_open_dialog_in_the_files_folder_and_diffs_the_picked_file()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        OpenView? view = null;
        IReadOnlyList<string> rows = [];

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.C),
            () => host.App.InjectKey(Key.T),
            () => host.App.InjectKey(Key.O),
            () => (view = Dialog(workbench)) is not null,
            () => { rows = view!.VisibleItems; },
            () => Type(host, "b.t"),
            () => view!.VisibleItems.SequenceEqual(["b.txt"]),
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is not null);

        Assert.Equal("Compare a.txt to…", view!.Title);
        Assert.Empty(view.SubViews.OfType<Terminal.Gui.Views.Button>());
        Assert.Equal(["../", "a.txt", "b.txt"], rows);
        Assert.Null(Dialog(workbench));
        var diff = Assert.Single(workbench.Editor.Group.DiffTabs);
        Assert.Equal("a.txt ↔ b.txt", diff.Title);
        Assert.Equal([DiffRowKind.Both, DiffRowKind.Modified], diff.Diff.Rows.Select(r => r.Kind).Take(2));
    }

    [Fact]
    public async Task Esc_cancels_without_opening_a_tab()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToOtherFile),
            () => Dialog(workbench) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => Dialog(workbench) is null);

        Assert.Empty(workbench.Editor.Group.DiffTabs);
        Assert.NotNull(workbench.Editor.Group.ActiveTab);
    }

    [Theory]
    [InlineData("a.t", "Can't compare a.txt with itself")]
    [InlineData("same", "No changes against same.txt")]
    public async Task Picking_the_same_or_an_identical_file_says_so_and_opens_no_tab(string query, string message)
    {
        _fs.AddFile("/work/src/same.txt", new MockFileData("alpha\nbravo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, [
            () => OpenFile(workbench),
            .. Pick(host, commands, workbench, query),
            () => workbench.StatusBar.DisplayedText == message]);

        Assert.Empty(workbench.Editor.Group.DiffTabs);
        Assert.Null(Dialog(workbench));
    }

    [Fact]
    public async Task A_file_that_cant_be_read_says_so_and_opens_no_tab()
    {
        _fs.GetFile("/work/src/b.txt").AllowedFileShare = FileShare.None;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, [
            () => OpenFile(workbench),
            .. Pick(host, commands, workbench, "b.t"),
            () => workbench.StatusBar.DisplayedText.StartsWith("Can't read b.txt:")]);

        Assert.Empty(workbench.Editor.Group.DiffTabs);
    }

    [Fact]
    public async Task Files_with_the_same_name_in_different_folders_get_their_own_tabs()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, [
            () => OpenFile(workbench),
            .. Pick(host, commands, workbench, "b.t"),
            () => workbench.Editor.Group.DiffTabs.Count == 1,
            .. Pick(host, commands, workbench, "oth/b", fromParent: true),
            () => workbench.Editor.Group.DiffTabs.Count == 2]);

        Assert.Equal(
            [_fs.FileInfo.New("/work/src/b.txt").FullName, _fs.FileInfo.New("/work/other/b.txt").FullName],
            workbench.Editor.Group.DiffTabs.Select(d => d.LeftKey));
        Assert.All(workbench.Editor.Group.DiffTabs, d => Assert.Equal("a.txt ↔ b.txt", d.Title));
    }

    [Fact]
    public async Task The_left_side_reloads_from_disk_when_the_tab_becomes_active_again()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host, [
            () => OpenFile(workbench),
            .. Pick(host, commands, workbench, "b.t"),
            () => group.ActiveDiffTab is not null,
            () =>
            {
                _fs.File.WriteAllText("/work/src/b.txt", "alpha\nbravo\ncharlie\n");
                OpenFile(workbench);
                group.Value = group.DiffTabs[0];
            },
            () => group.ActiveDiffTab is not null]);

        Assert.Equal([DiffRowKind.Both, DiffRowKind.Both, DiffRowKind.LeftOnly], group.DiffTabs[0].Diff.Rows.Select(r => r.Kind).Take(3));
    }

    [Fact]
    public async Task Cto_with_no_file_open_says_so_and_opens_no_dialog()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.CompareToOtherFile),
            () => workbench.StatusBar.DisplayedText == "No file is open.");

        Assert.Null(Dialog(workbench));
    }

    private static OpenView? Dialog(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<OpenView>().SingleOrDefault();

    private static Delegate[] Pick(WorkbenchHost host, CommandService commands, Workbench.Workbench workbench, string query, bool fromParent = false) =>
    [
        () => commands.TryExecute(CommandIds.CompareToOtherFile),
        () => Dialog(workbench) is not null,
        () => { if (fromParent) host.App.InjectKey(Key.Enter); },
        () => Type(host, query),
        () => Dialog(workbench)!.VisibleItems.Count == 1,
        () => host.App.InjectKey(Key.Enter),
        () => Dialog(workbench) is null,
    ];

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    private void OpenFile(Workbench.Workbench workbench) => workbench.OpenFile(_fs.FileInfo.New("/work/src/a.txt"));

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
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
