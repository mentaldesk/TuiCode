using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Closing a tab with unsaved edits asks first (#409). Boots a TG Application — serialised (#77).
public class CloseDirtyTabHostTests : StaticConfigurationTest
{
    private const string Path = "/work/a.txt";

    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly CountingScopes _scopes = new();

    [Fact]
    public async Task Closing_a_dirty_tab_asks_first_with_cancel_focused()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var title = "";
        var asked = "";
        var focused = "";
        string[] buttons = [];

        await HostSteps.Run(host,
            () => { OpenDirty(workbench); },
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => Confirm(workbench) is not null,
            () =>
            {
                var view = Confirm(workbench)!;
                (title, asked, focused) = (view.Title, Message(view), view.FocusedChoice!);
                buttons = [.. view.SubViews.OfType<Button>().Select(b => b.Text)];
            });

        Assert.Equal("Unsaved changes", title);
        Assert.Equal(
            """
            Save changes to 'a.txt' before closing?
            Your changes will be lost if you don't save them.
            """.ReplaceLineEndings("\n"),
            asked.ReplaceLineEndings("\n"));
        Assert.Equal(["Save", "Don't save", "Cancel"], buttons);
        Assert.Equal("Cancel", focused);
        Assert.NotNull(workbench.Editor.Group.ActiveTab);
    }

    [Theory]
    [InlineData(false)] // Enter lands on the focused Cancel
    [InlineData(true)]
    public async Task Cancelling_leaves_the_tab_open_and_dirty(bool escape)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { OpenDirty(workbench); },
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(escape ? Key.Esc : Key.Enter),
            () => Confirm(workbench) is null);

        Assert.True(workbench.Editor.Group.ActiveTab!.IsDirty);
        Assert.Equal("one\n", _fs.File.ReadAllText(Path));
        Assert.Equal(1, _scopes.Depth);
    }

    [Fact]
    public async Task Save_writes_the_file_and_closes_the_tab()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { OpenDirty(workbench); },
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Empty(workbench.Editor.Group.Tabs);
        Assert.Equal("mine\n", _fs.File.ReadAllText(Path));
        Assert.Equal(1, _scopes.Depth);
    }

    [Fact]
    public async Task Dont_save_closes_the_tab_and_leaves_the_file_alone()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? closed = null;

        await HostSteps.Run(host,
            () => { closed = OpenDirty(workbench); },
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Don't save"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null,
            () => workbench.OpenFile(_fs.FileInfo.New(Path)));

        var reopened = workbench.Editor.Group.ActiveTab!;
        Assert.NotSame(closed, reopened);
        Assert.Equal("one\n", reopened.Content.ReplaceLineEndings("\n"));
        Assert.False(reopened.IsDirty);
        Assert.Equal("one\n", _fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task Save_over_a_file_that_changed_closes_only_once_overwritten()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var conflict = "";

        await HostSteps.Run(host,
            () => OpenDirtyWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench)?.Title == "File changed on disk",
            () => { conflict = Confirm(workbench)!.Title; },
            Reach(host, workbench, "Overwrite"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Equal("File changed on disk", conflict);
        Assert.Empty(workbench.Editor.Group.Tabs);
        Assert.Equal("mine\n", _fs.File.ReadAllText(Path));
        Assert.Equal(1, _scopes.Depth);
    }

    [Theory]
    [InlineData("Compare")]
    [InlineData("Reload")]
    [InlineData("Cancel")]
    public async Task Save_over_a_file_that_changed_keeps_the_tab_unless_overwritten(string choice)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenDirtyWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench)?.Title == "File changed on disk",
            Reach(host, workbench, choice),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Single(workbench.Editor.Group.Tabs);
        Assert.Equal("from the other branch\n", _fs.File.ReadAllText(Path));
        Assert.Equal(1, _scopes.Depth);
    }

    [Fact]
    public async Task The_close_command_asks_as_ctrl_w_does()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { OpenDirty(workbench); },
            () => _commands.TryExecute(CommandIds.CloseActiveEditor),
            () => Confirm(workbench) is not null);

        Assert.NotNull(workbench.Editor.Group.ActiveTab);
        Assert.Equal("cf", CommandMnemonics.For(CommandIds.CloseActiveEditor));
    }

    [Fact]
    public async Task A_clean_tab_closes_without_asking()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                _fs.AddFile(Path, new MockFileData("one\n"));
                workbench.OpenFile(_fs.FileInfo.New(Path));
            },
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => workbench.Editor.Group.ActiveTab is null);

        Assert.Null(Confirm(workbench));
    }

    [Fact]
    public async Task A_diff_tab_of_a_dirty_file_closes_without_asking()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { OpenDirty(workbench); },
            () => _commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.Editor.Group.ActiveDiffTab is not null,
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => workbench.Editor.Group.ActiveDiffTab is null);

        Assert.Null(Confirm(workbench));
        Assert.True(Assert.Single(workbench.Editor.Group.Tabs).IsDirty);
    }

    [Fact]
    public async Task A_document_tab_closes_without_asking()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => { workbench.Editor.Group.OpenDocument(_fs.FileInfo.New("/work/#1 Overview.md"), "# Hello\n"); },
            () => workbench.Editor.Group.ActiveDocumentTab is not null,
            () => host.App.InjectKey(Key.W.WithCtrl),
            () => workbench.Editor.Group.ActiveDocumentTab is null);

        Assert.Null(Confirm(workbench));
    }

    // Cancel starts focused, so Tab round the row until the wanted button has it.
    private static Func<bool> Reach(WorkbenchHost host, Workbench.Workbench workbench, string label) => () =>
    {
        if (Confirm(workbench)?.FocusedChoice == label) return true;
        host.App.InjectKey(Key.Tab);
        return false;
    };

    private EditorTab OpenDirty(Workbench.Workbench workbench)
    {
        _fs.AddFile(Path, new MockFileData("one\n"));
        workbench.OpenFile(_fs.FileInfo.New(Path));
        var tab = workbench.Editor.Group.ActiveTab!;
        tab.Content = "mine\n";
        return tab;
    }

    private void OpenDirtyWithAnExternalChange(Workbench.Workbench workbench)
    {
        OpenDirty(workbench);
        _fs.File.WriteAllText(Path, "from the other branch\n");
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

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, new KeybindingService(_commands), _scopes,
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);

    private sealed class CountingScopes : IInputScopeStack
    {
        private readonly InputScopeStack _inner = new();

        public int Depth { get; private set; }

        public void Push(IKeybindingService scope) { _inner.Push(scope); Depth++; }

        public void Pop(IKeybindingService scope) { _inner.Pop(scope); Depth--; }

        public KeyHandlingResult Handle(Key key) => _inner.Handle(key);
    }
}
