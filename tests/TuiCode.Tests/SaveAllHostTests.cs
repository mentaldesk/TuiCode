using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Save all (#410). Boots a TG Application — serialised (#77).
public class SaveAllHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;

    public SaveAllHostTests() => _keybindings = new KeybindingService(_commands);

    [Fact]
    public void Is_a_global_command_with_its_mnemonic_key_and_place_under_save()
    {
        using var workbench = BuildWorkbench();
        using var _ = BuildHost(workbench);

        var command = Assert.Single(_commands.Registered, c => c.Id == CommandIds.SaveAll);
        Assert.Equal("Save all", command.Label);
        Assert.Equal(CommandScope.Global, command.Scope);
        Assert.Equal("saf", CommandMnemonics.For(CommandIds.SaveAll));
        var binding = Assert.Single(_keybindings.Bindings, b => b.CommandId == CommandIds.SaveAll);
        Assert.Equal("Ctrl+Shift+S", binding.Display);
        var file = CommandMenu.Layout.Single(m => m.Title == "_File").Ids;
        Assert.Equal(CommandIds.SaveAll, file[Array.IndexOf(file, CommandIds.SaveActiveEditor) + 1]);
    }

    [Fact]
    public async Task Writes_every_dirty_tab_and_reports_how_many()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "/work/a.txt", edit: "mine a\n");
                Open(workbench, "/work/b.txt", edit: "mine b\n");
                Open(workbench, "/work/c.txt", edit: null);
                Open(workbench, "/work/d.txt", edit: "mine d\n");
            },
            () => host.App.InjectKey(Key.S.WithCtrl.WithShift),
            () => workbench.StatusBar.Message == "Saved 3 files");

        Assert.Equal("mine a\n", _fs.File.ReadAllText("/work/a.txt"));
        Assert.Equal("mine b\n", _fs.File.ReadAllText("/work/b.txt"));
        Assert.Equal("one\n", _fs.File.ReadAllText("/work/c.txt"));
        Assert.Equal("mine d\n", _fs.File.ReadAllText("/work/d.txt"));
        Assert.DoesNotContain(workbench.Editor.Group.Tabs, t => t.IsDirty);
    }

    [Fact]
    public async Task One_file_is_reported_in_the_singular()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt", edit: "mine\n"),
            () => _commands.TryExecute(CommandIds.SaveAll),
            () => workbench.StatusBar.Message == "Saved 1 file");

        Assert.Equal("mine\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task Nothing_dirty_writes_nothing_and_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var written = DateTime.MinValue;

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "/work/a.txt", edit: null);
                written = _fs.File.GetLastWriteTimeUtc("/work/a.txt");
            },
            () => host.App.InjectKey(Key.S.WithCtrl.WithShift),
            () => workbench.StatusBar.Message == "No unsaved changes");

        Assert.Equal(written, _fs.File.GetLastWriteTimeUtc("/work/a.txt"));
    }

    [Fact]
    public async Task A_file_changed_on_disk_gets_the_conflict_prompt_while_the_rest_save()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "/work/a.txt", edit: "mine a\n");
                Open(workbench, "/work/b.txt", edit: "mine b\n");
                _fs.File.WriteAllText("/work/a.txt", "from the other branch\n");
            },
            () => host.App.InjectKey(Key.S.WithCtrl.WithShift),
            () => Confirm(workbench) is not null,
            () => { asked = Confirm(workbench)!.SubViews.OfType<Label>().First().Text; });

        Assert.StartsWith("'a.txt' changed on disk since you opened it.", asked);
        Assert.Equal("Saved 1 file", workbench.StatusBar.Message);
        Assert.Equal("from the other branch\n", _fs.File.ReadAllText("/work/a.txt"));
        Assert.Equal("mine b\n", _fs.File.ReadAllText("/work/b.txt"));
    }

    [Fact]
    public async Task Overwriting_from_the_prompt_writes_the_conflicting_file()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "/work/a.txt", edit: "mine\n");
                _fs.File.WriteAllText("/work/a.txt", "from the other branch\n");
            },
            () => _commands.TryExecute(CommandIds.SaveAll),
            () => Confirm(workbench) is not null,
            () =>
            {
                if (Confirm(workbench)?.FocusedChoice == "Overwrite") return true;
                host.App.InjectKey(Key.Tab);
                return false;
            },
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Equal("mine\n", _fs.File.ReadAllText("/work/a.txt"));
        Assert.NotEqual("Saved 1 file", workbench.StatusBar.Message);
    }

    [Fact]
    public async Task An_open_diff_is_left_alone_while_its_file_saves()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt", edit: "mine\n"),
            () => _commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.Editor.Group.ActiveDiffTab is not null,
            () => _commands.TryExecute(CommandIds.SaveAll),
            () => workbench.StatusBar.Message == "Saved 1 file");

        Assert.Equal("mine\n", _fs.File.ReadAllText("/work/a.txt"));
        Assert.Single(workbench.Editor.Group.DiffTabs);
    }

    private void Open(Workbench.Workbench workbench, string path, string? edit)
    {
        _fs.AddFile(path, new MockFileData("one\n"));
        workbench.OpenFile(_fs.FileInfo.New(path));
        if (edit is not null) workbench.Editor.Group.ActiveTab!.Content = edit;
    }

    private static ConfirmView? Confirm(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ConfirmView>().SingleOrDefault();

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, _keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
}
