using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class NavigationKeybindingsTests
{
    [Fact]
    public async Task CtrlG_opens_the_go_to_line_overlay()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("line1\nline2\nline3\n"));
        workbench.Editor.Group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        var modalAppeared = false;

        host.App.Iteration += OnFirst;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested);

        Assert.True(modalAppeared, "GoToLineView did not mount after Ctrl+G");

        void OnFirst(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirst;
            if (Key.TryParse("Ctrl+G", out var k))
                host.App.InjectKey(k);
            host.App.Iteration += OnSecond;
        }

        void OnSecond(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecond;
            modalAppeared = workbench.SubViews.OfType<GoToLineView>().Any();
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    [Fact]
    public async Task AltLeft_navigates_back_to_the_previously_active_tab()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("alpha"));
        fs.AddFile("/work/b.txt", new MockFileData("bravo"));
        var firstTab = workbench.Editor.Group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        var secondTab = workbench.Editor.Group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));
        // Active is now b. Pressing Alt+Left should swap us back to a (history records the leaving
        // location automatically when Group.ActiveTab changes).

        host.App.Iteration += OnFirst;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested);

        Assert.Same(firstTab, workbench.Editor.Group.ActiveTab);

        void OnFirst(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirst;
            if (Key.TryParse("Alt+CursorLeft", out var k))
                host.App.InjectKey(k);
            host.App.Iteration += OnSecond;
        }

        void OnSecond(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecond;
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    [Fact]
    public async Task AltRight_with_no_forward_history_is_a_noop()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("alpha"));
        var only = workbench.Editor.Group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        host.App.Iteration += OnFirst;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested);

        Assert.Same(only, workbench.Editor.Group.ActiveTab);

        void OnFirst(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirst;
            if (Key.TryParse("Alt+CursorRight", out var k))
                host.App.InjectKey(k);
            host.App.Iteration += OnSecond;
        }

        void OnSecond(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecond;
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    private static Workbench.Workbench BuildWorkbench() =>
        new(
            new SidebarPart(new FileExplorerView()),
            new EditorPart(),
            new StatusBarPart());
}
