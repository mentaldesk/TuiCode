using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Reload from disk on demand (#272). Boots a TG Application — serialised (#77).
public class ReloadFromDiskHostTests : StaticConfigurationTest
{
    private const string Path = "/work/a.txt";

    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly CountingScopes _scopes = new();
    private readonly KeybindingService _keybindings;

    public ReloadFromDiskHostTests() => _keybindings = new KeybindingService(_commands);

    [Fact]
    public void Is_an_editor_command_with_a_label_mnemonic_and_no_default_key()
    {
        using var workbench = BuildWorkbench();
        using var _ = BuildHost(workbench);

        var command = Assert.Single(_commands.Registered, c => c.Id == CommandIds.ReloadFromDisk);
        Assert.Equal("Reload from disk", command.Label);
        Assert.Equal(CommandScope.Editor, command.Scope);
        Assert.Equal("rd", CommandMnemonics.For(CommandIds.ReloadFromDisk));
        Assert.DoesNotContain(_keybindings.Bindings, b => b.CommandId == CommandIds.ReloadFromDisk);
    }

    [Fact]
    public async Task Is_not_enabled_with_no_tab_or_on_a_diff_tab()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var withNoTab = true;

        await HostSteps.Run(host,
            () => { withNoTab = _commands.IsEnabled(CommandIds.ReloadFromDisk); },
            () => OpenWithAnExternalChange(workbench, dirty: true),
            () => _commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.Editor.Group.ActiveDiffTab is not null);

        Assert.False(withNoTab);
        Assert.False(_commands.IsEnabled(CommandIds.ReloadFromDisk));
    }

    [Fact]
    public async Task A_clean_tab_reloads_without_asking()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench, dirty: false),
            () => { workbench.Editor.Group.ActiveTab!.MarkOnDisk(DiskState.Changed); },
            () => _commands.TryExecute(CommandIds.ReloadFromDisk));

        var tab = workbench.Editor.Group.ActiveTab!;
        Assert.Null(Confirm(workbench));
        Assert.Equal("theirs\n", tab.Content.ReplaceLineEndings("\n"));
        Assert.False(tab.IsDirty);
        Assert.Equal(DiskState.Unchanged, tab.DiskMarker);
        Assert.Equal("⟳ Reloaded a.txt from disk", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task A_file_that_has_not_changed_still_reloads_rather_than_erroring()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                _fs.AddFile(Path, new MockFileData("one\n"));
                workbench.OpenFile(_fs.FileInfo.New(Path));
            },
            () => workbench.Editor.Group.ActiveTab is not null,
            () => _commands.TryExecute(CommandIds.ReloadFromDisk));

        Assert.Equal("one\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
        Assert.Equal("⟳ Reloaded a.txt from disk", workbench.StatusBar.DisplayedText);
    }

    [Theory]
    [InlineData(false)] // Enter lands on the focused Cancel
    [InlineData(true)]
    public async Task A_dirty_tab_asks_first_and_cancelling_changes_nothing(bool escape)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var focused = "";

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench, dirty: true),
            () => _commands.TryExecute(CommandIds.ReloadFromDisk),
            () => Confirm(workbench) is not null,
            () => { focused = Confirm(workbench)!.FocusedChoice!; },
            () => host.App.InjectKey(escape ? Key.Esc : Key.Enter),
            () => Confirm(workbench) is null);

        var tab = workbench.Editor.Group.ActiveTab!;
        Assert.Equal("Cancel", focused);
        Assert.Equal("mine\n", tab.Content.ReplaceLineEndings("\n"));
        Assert.True(tab.IsDirty);
        Assert.Equal(1, _scopes.Depth);
    }

    [Fact]
    public async Task A_dirty_tab_takes_what_is_on_disk_when_you_choose_reload()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string[] buttons = [];

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench, dirty: true),
            () => _commands.TryExecute(CommandIds.ReloadFromDisk),
            () => Confirm(workbench) is not null,
            () => { buttons = [.. Confirm(workbench)!.SubViews.OfType<Button>().Select(b => b.Text)]; },
            Reach(host, workbench, "Reload"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        var tab = workbench.Editor.Group.ActiveTab!;
        Assert.Equal(["Reload", "Cancel"], buttons);
        Assert.Equal("theirs\n", tab.Content.ReplaceLineEndings("\n"));
        Assert.False(tab.IsDirty);
        Assert.Equal(1, _scopes.Depth);
    }

    // The snapshot still matches the file, so only the edits stand between the buffer and the disk.
    [Fact]
    public async Task A_dirty_tab_over_an_unchanged_file_drops_its_edits_when_you_choose_reload()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                _fs.AddFile(Path, new MockFileData("one\n"));
                workbench.OpenFile(_fs.FileInfo.New(Path));
                workbench.Editor.Group.ActiveTab!.Content = "mine\n";
            },
            () => _commands.TryExecute(CommandIds.ReloadFromDisk),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Reload"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        var tab = workbench.Editor.Group.ActiveTab!;
        Assert.Equal("one\n", tab.Content.ReplaceLineEndings("\n"));
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public async Task A_file_that_has_gone_is_refused_and_keeps_its_buffer()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench, dirty: true),
            () => _fs.File.Delete(Path),
            () => _commands.TryExecute(CommandIds.ReloadFromDisk));

        var tab = workbench.Editor.Group.ActiveTab!;
        Assert.Null(Confirm(workbench));
        Assert.Equal("mine\n", tab.Content.ReplaceLineEndings("\n"));
        Assert.True(tab.IsDirty);
        Assert.Equal("a.txt no longer exists on disk — nothing to reload", workbench.StatusBar.DisplayedText);
    }

    private static Func<bool> Reach(WorkbenchHost host, Workbench.Workbench workbench, string label) => () =>
    {
        if (Confirm(workbench)?.FocusedChoice == label) return true;
        host.App.InjectKey(Key.Tab);
        return false;
    };

    private void OpenWithAnExternalChange(Workbench.Workbench workbench, bool dirty)
    {
        _fs.AddFile(Path, new MockFileData("one\n"));
        workbench.OpenFile(_fs.FileInfo.New(Path));
        if (dirty) workbench.Editor.Group.ActiveTab!.Content = "mine\n";
        _fs.File.WriteAllText(Path, "theirs\n");
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
        new(workbench, _commands, _keybindings, _scopes,
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
