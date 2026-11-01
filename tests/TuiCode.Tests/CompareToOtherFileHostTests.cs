using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using MentalDesk.Tui.Paths;
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
    public async Task Cto_opens_a_file_picker_in_the_files_folder_and_diffs_the_picked_file()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        PathPicker? view = null;
        IReadOnlyList<string> buttons = [];

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.C),
            () => host.App.InjectKey(Key.T),
            () => host.App.InjectKey(Key.O),
            () => (view = Dialog(workbench)) is not null,
            () => { buttons = PathPickers.Buttons(view!); },
            () => Choose(view!, "/work/src/b.txt"),
            () => workbench.Editor.Group.ActiveDiffTab is not null);

        Assert.Equal("Compare a.txt to…", view!.Title);
        Assert.Equal(_fs.Path.GetFullPath("/work/src"), view.Folder);
        Assert.Equal(["Enter Open", "Esc Cancel"], buttons);
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
    [InlineData("/work/src/a.txt", "Can't compare a.txt with itself")]
    [InlineData("/work/src/same.txt", "No changes against same.txt")]
    public async Task Picking_the_same_or_an_identical_file_says_so_and_opens_no_tab(string path, string message)
    {
        _fs.AddFile("/work/src/same.txt", new MockFileData("alpha\nbravo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host, [
            () => OpenFile(workbench),
            .. Pick(commands, workbench, path),
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
            .. Pick(commands, workbench, "/work/src/b.txt"),
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
            .. Pick(commands, workbench, "/work/src/b.txt"),
            () => workbench.Editor.Group.DiffTabs.Count == 1,
            .. Pick(commands, workbench, "/work/other/b.txt"),
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
            .. Pick(commands, workbench, "/work/src/b.txt"),
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

    private static PathPicker? Dialog(Workbench.Workbench workbench) => PathPickers.Showing(workbench);

    private void Choose(PathPicker picker, string path) => picker.Choose(new PathRow(PathRowKind.File, _fs.Path.GetFullPath(path), path));

    private Delegate[] Pick(CommandService commands, Workbench.Workbench workbench, string path) =>
    [
        () => commands.TryExecute(CommandIds.CompareToOtherFile),
        () => Dialog(workbench) is not null,
        () => Choose(Dialog(workbench)!, path),
        () => Dialog(workbench) is null,
    ];

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
