using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Undo close tab (#578). Boots a TG Application — serialised (#77).
public class UndoCloseTabHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;

    public UndoCloseTabHostTests()
    {
        _keybindings = new KeybindingService(_commands);
        _fs.AddDirectory("/work");
        foreach (var name in new[] { "a.txt", "b.txt" })
            _fs.AddFile($"/work/{name}", new MockFileData("one\n"));
    }

    [Fact]
    public void It_follows_close_all_tabs_in_the_file_menu_on_ctrl_shift_t()
    {
        using var workbench = BuildWorkbench();
        using var _ = BuildHost(workbench);

        var file = CommandMenu.Layout.Single(m => m.Title == "_File").Ids;
        Assert.Equal(CommandIds.ReopenClosedEditor, file[Array.IndexOf(file, CommandIds.CloseAllTabs) + 1]);
        Assert.Equal('u', CommandMenu.Letters[CommandIds.ReopenClosedEditor]);
        Assert.Equal("uct", CommandMnemonics.For(CommandIds.ReopenClosedEditor));
        Assert.Equal("Undo close tab", _commands.Registered.Single(c => c.Id == CommandIds.ReopenClosedEditor).Label);
        Assert.Contains(_keybindings.Bindings, b => b.CommandId == CommandIds.ReopenClosedEditor && b.Display == "Ctrl+Shift+T");
        Assert.Contains("reopen", CommandIds.ReopenClosedEditor, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ctrl_shift_t_after_closing_the_last_tab_brings_it_back_with_the_keys()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var enabled = new List<bool>();

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => enabled.Add(_commands.IsEnabled(CommandIds.ReopenClosedEditor)),
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => workbench.Editor.Group.Value is null,
            () => enabled.Add(_commands.IsEnabled(CommandIds.ReopenClosedEditor)),
            () => host.App.InjectKey(Key.T.WithCtrl.WithShift),
            () => workbench.Editor.Group.ActiveTab is not null && workbench.StatusBar.DisplayedFocus == "Editor",
            () => enabled.Add(_commands.IsEnabled(CommandIds.ReopenClosedEditor)));

        Assert.Equal([false, true, false], enabled);
        Assert.Equal("a.txt", workbench.Editor.Group.ActiveTab!.File.Name);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, _keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
}
