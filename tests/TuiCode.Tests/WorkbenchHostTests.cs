using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class WorkbenchHostTests
{
    [Fact]
    public async Task CtrlQ_quits_the_workbench()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = host.RunAsync(cts.Token);

        await runTask;
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out — Ctrl+Q did not stop the loop");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task Ctrl0_toggles_sidebar_visibility()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.False(workbench.IsSidebarVisible);

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+D0", out var ctrl0))
                host.App.InjectKey(ctrl0);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task Ctrl1_focuses_first_open_editor_tab()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        var firstTab = workbench.Editor.Group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        workbench.Editor.Group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));
        // Active is now b. Ctrl+1 should switch to a.

        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.Same(firstTab, workbench.Editor.Group.ActiveTab);

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+D1", out var ctrl1))
                host.App.InjectKey(ctrl1);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task Ctrl5_with_only_two_tabs_is_a_noop()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        workbench.Editor.Group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        var second = workbench.Editor.Group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));
        // Active is now b.

        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.Same(second, workbench.Editor.Group.ActiveTab);

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+D5", out var ctrl5))
                host.App.InjectKey(ctrl5);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task CtrlComma_opens_the_settings_overlay()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var settingsViewWasMounted = false;
        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(settingsViewWasMounted, "SettingsView did not appear in the workbench after Ctrl+,");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+,", out var ctrlComma))
                host.App.InjectKey(ctrlComma);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            settingsViewWasMounted = workbench.SubViews.OfType<TuiCode.Workbench.Settings.SettingsView>().Any();
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task CtrlE_opens_the_action_overlay()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var actionViewWasMounted = false;
        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(actionViewWasMounted, "ActionView did not appear in the workbench after Ctrl+E");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+E", out var key))
                host.App.InjectKey(key);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            actionViewWasMounted = workbench.SubViews.OfType<TuiCode.Workbench.Actions.ActionView>().Any();
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task Esc_closes_the_action_overlay_without_executing()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var actionViewWasGone = false;
        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(actionViewWasGone, "ActionView was still mounted after Esc");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+E", out var key))
                host.App.InjectKey(key);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            if (Key.TryParse("Esc", out var esc))
                host.App.InjectKey(esc);
            host.App.Iteration += OnThirdIteration;
        }

        void OnThirdIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnThirdIteration;
            actionViewWasGone = !workbench.SubViews.OfType<TuiCode.Workbench.Actions.ActionView>().Any();
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task F1_opens_the_help_dialog()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var helpViewWasMounted = false;
        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(helpViewWasMounted, "HelpView did not appear in the workbench after F1");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("F1", out var key))
                host.App.InjectKey(key);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            helpViewWasMounted = workbench.SubViews.OfType<TuiCode.Workbench.Help.HelpView>().Any();
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task Esc_closes_the_help_dialog()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        var history = new NavigationHistoryService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, history);

        var helpViewWasGone = false;
        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(helpViewWasGone, "HelpView was still mounted after Esc");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("F1", out var key))
                host.App.InjectKey(key);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            if (Key.TryParse("Esc", out var esc))
                host.App.InjectKey(esc);
            host.App.Iteration += OnThirdIteration;
        }

        void OnThirdIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnThirdIteration;
            helpViewWasGone = !workbench.SubViews.OfType<TuiCode.Workbench.Help.HelpView>().Any();
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    private static Workbench.Workbench BuildWorkbench() =>
        new(
            new SidebarPart(new FileExplorerView()),
            new EditorPart(),
            new StatusBarPart());
}
