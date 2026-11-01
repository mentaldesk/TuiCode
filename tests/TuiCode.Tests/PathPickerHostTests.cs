using MentalDesk.Tui.Paths;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Icons;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// PathPicker reads the real disk, so these pick rows on the mock directly. Boots a TG Application — serialised (#77).
public class PathPickerHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FileIcons _icons = new(() => new FontDetection(true, "test"));

    public PathPickerHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/src/b.cs", new MockFileData("b"));
        _fs.AddFile("/work/docs/c.md", new MockFileData("c"));
    }

    [Fact]
    public async Task Ctrl_O_opens_an_Open_picker_at_the_root_and_Ctrl_Enter_opens_that_folder()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        PathPicker? picker = null;
        IReadOnlyList<string> buttons = [];

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.O.WithCtrl),
            () => (picker = PathPickers.Showing(workbench)) is not null,
            () => { buttons = PathPickers.Buttons(picker!); },
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => PathPickers.Showing(workbench) is null);

        Assert.Equal($"Opened folder: {Full("/work")}", workbench.StatusBar.DisplayedText);
        Assert.Equal("Open", picker!.Title);
        Assert.Equal(Full("/work"), picker.Folder);
        Assert.Equal(["Enter Open", "Ctrl+Enter Open this folder", "Esc Cancel"], buttons);
    }

    [Fact]
    public async Task The_file_icon_setting_reaches_the_pickers_rows()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var row = new PathRow(PathRowKind.File, Full("/work/a.txt"), "a.txt");
        var folder = new PathRow(PathRowKind.Folder, Full("/work/src"), "src/");
        var fileGlyph = _icons.ForFile("a.txt")?.Glyph;
        var folderGlyph = _icons.ForDirectory(false)?.Glyph;
        string? on = null, folderIcon = null, off = "unset";

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.O.WithCtrl),
            () => PathPickers.Showing(workbench) is not null,
            () =>
            {
                var picker = PathPickers.Showing(workbench)!;
                on = picker.Icon!(row);
                folderIcon = picker.Icon!(folder);
                _icons.Setting = FileIconStyle.Off;
                off = picker.Icon!(row);
                host.App.InjectKey(Key.Esc);
            },
            () => PathPickers.Showing(workbench) is null);

        Assert.NotNull(fileGlyph);
        Assert.Equal(fileGlyph, on);
        Assert.Equal(folderGlyph, folderIcon);
        Assert.Null(off);
    }

    [Fact]
    public async Task New_file_starts_in_the_folder_selected_in_the_explorer_and_creates_and_opens_the_named_file()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        PathPicker? picker = null;
        IReadOnlyList<string> buttons = [];

        await HostSteps.Run(host,
            () => Select(workbench, "src"),
            () => host.App.InjectKey(Key.N.WithCtrl),
            () => (picker = PathPickers.Showing(workbench)) is not null,
            () => { buttons = PathPickers.Buttons(picker!); },
            () => picker!.Choose(new PathRow(PathRowKind.NewFile, Full("/work/src/notes.md"), "New file: notes.md")),
            () => workbench.Editor.Group.ActiveTab is not null);

        Assert.Equal("New file", picker!.Title);
        Assert.Equal(Full("/work/src"), picker.Folder);
        Assert.Equal(["Ctrl+Enter Save here", "Esc Cancel"], buttons);
        Assert.True(_fs.File.Exists("/work/src/notes.md"));
        Assert.Equal(Full("/work/src/notes.md"), workbench.Editor.Group.ActiveTab!.File.FullName);
    }

    [Fact]
    public async Task New_file_from_the_editor_starts_in_the_active_files_folder()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        PathPicker? picker = null;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/docs/c.md")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => commands.TryExecute(CommandIds.New),
            () => (picker = PathPickers.Showing(workbench)) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => PathPickers.Showing(workbench) is null);

        Assert.Equal(Full("/work/docs"), picker!.Folder);
    }

    [Theory]
    [InlineData("Replace", "")]
    [InlineData("Cancel", "a")]
    public async Task Naming_a_file_that_exists_asks_before_replacing_it(string choice, string content)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var asked = "";
        var picking = false;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.New),
            () => PathPickers.Showing(workbench) is not null,
            () => { picking = PathPickers.Showing(workbench)!.ConfirmReplace!(Full("/work/a.txt")); },
            () => ConfirmDialogs.Of(workbench) is not null,
            () => { asked = ConfirmDialogs.Of(workbench)!.Message(); },
            ConfirmDialogs.Choose(host, workbench, choice),
            () => ConfirmDialogs.Of(workbench) is null);

        Assert.False(picking);
        Assert.Equal($"{Full("/work/a.txt")} is already there.\nReplace it? This can't be undone.", asked);
        Assert.Equal(content, _fs.File.ReadAllText("/work/a.txt"));
        Assert.Equal(choice == "Cancel", PathPickers.Showing(workbench) is not null);
        Assert.Equal(choice == "Replace", workbench.Editor.Group.ActiveTab?.File.Name == "a.txt");
    }

    [Fact]
    public async Task Cancelling_New_file_leaves_nothing_behind()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var before = _fs.AllPaths.ToList();

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.New),
            () => PathPickers.Showing(workbench) is not null,
            () => { foreach (var c in "notes.md") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Esc),
            () => PathPickers.Showing(workbench) is null);

        Assert.Equal(before, _fs.AllPaths);
        Assert.Null(workbench.Editor.Group.ActiveTab);
    }

    [Fact]
    public async Task New_folder_is_its_own_command_on_the_typed_prompt()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        string? title = null;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.NewFolder),
            () => Prompt(workbench) is not null,
            () =>
            {
                title = Prompt(workbench)!.Title;
                foreach (var c in "lib") host.App.InjectKey(new Key(c));
            },
            () => host.App.InjectKey(Key.Enter),
            () => Prompt(workbench) is null);

        Assert.Equal("New folder", title);
        Assert.True(_fs.Directory.Exists("/work/lib"));
        Assert.Equal("lib", workbench.Sidebar.Explorer.SelectedObject?.Name);
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private static PathPromptView? Prompt(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<PathPromptView>().SingleOrDefault();

    private static void Select(Workbench.Workbench workbench, string name)
    {
        var explorer = workbench.Sidebar.Explorer;
        explorer.SetFocus();
        explorer.SelectedObject = explorer.GetChildren(explorer.Root!).Single(c => c.Name == name);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), icons: _icons, driverName: DriverRegistry.Names.ANSI);
    }
}
