using MentalDesk.Tui.Dialogs;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// The keys every confirm shares (#524), driven through the real key path on "File changed on disk", the four-button one.
// Boots a TG Application — serialised (#77).
public class ConfirmKeysHostTests : StaticConfigurationTest
{
    private const string Path = "/work/a.txt";
    private const string Theirs = "from the other branch\n";

    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();

    [Theory]
    [InlineData(0, "Esc Cancel")]
    [InlineData(1, "Reload")]
    [InlineData(2, "Overwrite")]
    [InlineData(3, "Ctrl+Enter Compare")]
    public async Task Enter_does_nothing_whichever_button_has_focus(int lefts, string button)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var focused = "";
        var stillOpen = false;

        await HostSteps.Run(host,
            () => OpenConflict(workbench, host),
            () => ConfirmDialogs.Of(workbench) is not null,
            () => { for (var i = 0; i < lefts; i++) host.App.InjectKey(Key.CursorLeft); },
            () => { focused = ConfirmDialogs.Of(workbench)!.FocusedButton()!; host.App.InjectKey(Key.Enter); },
            () => { stillOpen = ConfirmDialogs.Of(workbench) is not null; host.App.InjectKey(Key.Esc); },
            () => ConfirmDialogs.Of(workbench) is null);

        Assert.Equal(button, focused);
        Assert.True(stillOpen, "Enter closed the dialog");
        Assert.Equal(Theirs, _fs.File.ReadAllText(Path));
        Assert.True(workbench.Editor.Group.ActiveTab!.IsDirty);
        Assert.Null(workbench.Editor.Group.ActiveDiffTab);
    }

    [Fact]
    public async Task Cancel_starts_focused_and_Left_Right_and_Tab_move_along_the_row()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        List<string> seen = [];

        void Note() => seen.Add(ConfirmDialogs.Of(workbench)!.FocusedButton()!);

        await HostSteps.Run(host,
            () => OpenConflict(workbench, host),
            () => ConfirmDialogs.Of(workbench) is not null,
            () => { Note(); host.App.InjectKey(Key.CursorLeft); },
            () => { Note(); host.App.InjectKey(Key.CursorLeft); },
            () => { Note(); host.App.InjectKey(Key.CursorRight); },
            () => { Note(); host.App.InjectKey(Key.Tab); },
            () => { Note(); host.App.InjectKey(Key.Esc); },
            () => ConfirmDialogs.Of(workbench) is null);

        Assert.Equal(["Esc Cancel", "Reload", "Overwrite", "Reload", "Esc Cancel"], seen);
    }

    [Fact]
    public async Task Space_presses_the_focused_button()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenConflict(workbench, host),
            () => ConfirmDialogs.Of(workbench) is not null,
            () => { host.App.InjectKey(Key.CursorLeft); host.App.InjectKey(Key.CursorLeft); },
            () => ConfirmDialogs.Of(workbench)!.FocusedButton() == "Overwrite",
            () => host.App.InjectKey(Key.Space),
            () => ConfirmDialogs.Of(workbench) is null);

        Assert.Equal("mine\n", _fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task Clicking_a_button_presses_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenConflict(workbench, host),
            () => ConfirmDialogs.Of(workbench) is not null,
            () =>
            {
                var dialog = ConfirmDialogs.Of(workbench)!;
                dialog.ButtonRow.Single(b => ConfirmDialogs.Caption(b) == "Reload").InvokeCommand(Command.Accept);
            },
            () => ConfirmDialogs.Of(workbench) is null);

        Assert.Equal(Theirs, workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
        Assert.False(workbench.Editor.Group.ActiveTab!.IsDirty);
    }

    [Theory]
    [InlineData(KeyCode.O, "mine\n")]
    [InlineData(KeyCode.R, Theirs)]
    public async Task A_letter_presses_its_button_without_Alt_whichever_button_has_focus(KeyCode letter, string content)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenConflict(workbench, host),
            () => ConfirmDialogs.Of(workbench) is not null,
            () => host.App.InjectKey(Key.CursorLeft),
            () => host.App.InjectKey(new Key(letter)),
            () => ConfirmDialogs.Of(workbench) is null);

        Assert.Equal(content, workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
        Assert.False(workbench.Editor.Group.ActiveTab!.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ctrl_Enter_presses_the_main_action_including_as_Windows_delivers_it(bool windows)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, windows);

        await HostSteps.Run(host,
            () => OpenConflict(workbench, host),
            () => ConfirmDialogs.Of(workbench) is not null,
            () => host.App.InjectKey(windows ? new Key(KeyCode.CtrlMask | (KeyCode)0x0A) : ConfirmDialogs.CtrlEnter),
            () => ConfirmDialogs.Of(workbench) is null);

        Assert.NotNull(workbench.Editor.Group.ActiveDiffTab);
        Assert.Equal(Theirs, _fs.File.ReadAllText(Path));
    }

    private void OpenConflict(Workbench.Workbench workbench, WorkbenchHost host)
    {
        _fs.AddFile(Path, new MockFileData("one\n"));
        workbench.OpenFile(_fs.FileInfo.New(Path));
        workbench.Editor.Group.ActiveTab!.Content = "mine\n";
        _fs.File.WriteAllText(Path, Theirs);
        host.App.InjectKey(Key.S.WithCtrl);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, bool windows = false) =>
        new(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            new InMemorySettingsService(), environment: new FakeEnvironment().SetIsWindows(windows),
            driverName: DriverRegistry.Names.ANSI);
}
