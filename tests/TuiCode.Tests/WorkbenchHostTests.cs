using Microsoft.Extensions.Logging;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Diagnostics;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Boots a TG Application via WorkbenchHost — process-global TG state, must be serialised (issue #77).
public class WorkbenchHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task CtrlQ_quits_the_workbench()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

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

    // #90: a hand-edited keybindings file can carry a malformed entry. Since #89 the chord is stored
    // by keycode (always a valid Key), so the bad part is now the *command* — here an empty string.
    // Applying it at startup must skip the bad entry rather than throw out of the constructor — the
    // app boots, the bad entry is logged as a warning (named by its display chord), and the surviving
    // default bindings (Ctrl+Q here) still work.
    [Fact]
    public async Task Malformed_keybinding_override_is_skipped_logged_and_does_not_crash_startup()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        settings.SetKeybindingOverrides(new[] { new KeybindingOverride(TestKeys.Chord("Ctrl+Alt+J"), "") });
        var logger = new ListLogger<WorkbenchHost>();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI, logger: logger);

        // Logged the moment ApplyKeybindings runs in the ctor — no need to wait for the run loop.
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Ctrl+Alt+J"));

        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out — startup choked on the malformed override or Ctrl+Q was lost");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
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
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

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
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

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
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

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
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

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
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

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
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

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
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

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

    [Fact]
    public async Task CtrlG_L_opens_the_go_to_line_overlay()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("line1\nline2\nline3\n"));
        workbench.Editor.Group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        var modalAppeared = false;
        host.App.Iteration += OnFirst;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested);

        Assert.True(modalAppeared, "GoToLineView did not mount after the Ctrl+G L chord");

        // Ctrl+G is now a chord prefix; the go-to-line modal only opens once L completes it.
        void OnFirst(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirst;
            if (Key.TryParse("Ctrl+G", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnSecond;
        }

        void OnSecond(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecond;
            if (Key.TryParse("L", out var l)) host.App.InjectKey(l);
            host.App.Iteration += OnThird;
        }

        void OnThird(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnThird;
            modalAppeared = workbench.SubViews.OfType<TuiCode.Workbench.Navigation.GoToLineView>().Any();
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    [Fact]
    public async Task CtrlO_opens_the_open_dialog()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var fs = new MockFileSystem();
        fs.AddDirectory("/work/src");
        fs.AddFile("/work/readme.md", new MockFileData("# hi"));
        workbench.Sidebar.Explorer.Open(fs.DirectoryInfo.New("/work"));

        var modalAppeared = false;
        host.App.Iteration += OnFirst;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(modalAppeared, "OpenView did not mount after Ctrl+O");

        void OnFirst(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirst;
            if (Key.TryParse("Ctrl+O", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnSecond;
        }

        void OnSecond(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecond;
            modalAppeared = workbench.SubViews.OfType<TuiCode.Workbench.Navigation.OpenView>().Any();
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    [Fact]
    public async Task Esc_closes_the_open_dialog()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var fs = new MockFileSystem();
        fs.AddFile("/work/readme.md", new MockFileData("# hi"));
        workbench.Sidebar.Explorer.Open(fs.DirectoryInfo.New("/work"));

        var modalWasGone = false;
        host.App.Iteration += OnFirst;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(modalWasGone, "OpenView was still mounted after Esc");

        void OnFirst(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirst;
            if (Key.TryParse("Ctrl+O", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnSecond;
        }

        void OnSecond(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecond;
            if (Key.TryParse("Esc", out var esc)) host.App.InjectKey(esc);
            host.App.Iteration += OnThird;
        }

        void OnThird(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnThird;
            modalWasGone = !workbench.SubViews.OfType<TuiCode.Workbench.Navigation.OpenView>().Any();
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    [Fact]
    public async Task F12_opens_the_diagnostics_dialog()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var diagnosticsViewWasMounted = false;
        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(diagnosticsViewWasMounted, "DiagnosticsView did not appear in the workbench after F12");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("F12", out var key))
                host.App.InjectKey(key);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            diagnosticsViewWasMounted = workbench.SubViews.OfType<DiagnosticsView>().Any();
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task Esc_closes_the_diagnostics_dialog()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var diagnosticsViewWasGone = false;
        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(diagnosticsViewWasGone, "DiagnosticsView was still mounted after Esc");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("F12", out var key))
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
            diagnosticsViewWasGone = !workbench.SubViews.OfType<DiagnosticsView>().Any();
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task CtrlN_opens_the_new_file_or_folder_dialog()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings);

        var fs = new MockFileSystem();
        fs.AddDirectory("/work/src");
        workbench.Sidebar.Explorer.Open(fs.DirectoryInfo.New("/work"));

        var modalAppeared = false;
        host.App.Iteration += OnFirst;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(modalAppeared, "NewPathView did not mount after Ctrl+N");

        void OnFirst(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirst;
            if (Key.TryParse("Ctrl+N", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnSecond;
        }

        void OnSecond(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecond;
            modalAppeared = workbench.SubViews.OfType<TuiCode.Workbench.Navigation.NewPathView>().Any();
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    [Fact]
    public async Task Esc_closes_the_new_file_or_folder_dialog()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings);

        var fs = new MockFileSystem();
        fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(fs.DirectoryInfo.New("/work"));

        var modalWasGone = false;
        host.App.Iteration += OnFirst;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(modalWasGone, "NewPathView was still mounted after Esc");

        void OnFirst(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirst;
            if (Key.TryParse("Ctrl+N", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnSecond;
        }

        void OnSecond(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecond;
            if (Key.TryParse("Esc", out var esc)) host.App.InjectKey(esc);
            host.App.Iteration += OnThird;
        }

        void OnThird(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnThird;
            modalWasGone = !workbench.SubViews.OfType<TuiCode.Workbench.Navigation.NewPathView>().Any();
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    [Fact]
    public async Task CtrlSpace_opens_the_mnemonic_overlay()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var mnemonicViewWasMounted = false;
        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(mnemonicViewWasMounted, "MnemonicView did not appear in the workbench after Ctrl+Space");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+Space", out var key))
                host.App.InjectKey(key);
            host.App.Iteration += OnSecondIteration;
        }

        void OnSecondIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSecondIteration;
            mnemonicViewWasMounted = workbench.SubViews.OfType<TuiCode.Workbench.Mnemonics.MnemonicView>().Any();
            // The overlay's capture scope swallows every key, so Esc it shut before Ctrl+Q —
            // otherwise Quit never reaches the workbench and RunAsync times out.
            if (Key.TryParse("Esc", out var esc))
                host.App.InjectKey(esc);
            host.App.Iteration += OnThirdIteration;
        }

        void OnThirdIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnThirdIteration;
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task Esc_closes_the_mnemonic_overlay()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var mnemonicViewWasGone = false;
        host.App.Iteration += OnFirstIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(mnemonicViewWasGone, "MnemonicView was still mounted after Esc");

        void OnFirstIteration(object? sender, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirstIteration;
            if (Key.TryParse("Ctrl+Space", out var key))
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
            mnemonicViewWasGone = !workbench.SubViews.OfType<TuiCode.Workbench.Mnemonics.MnemonicView>().Any();
            if (Key.TryParse("Ctrl+Q", out var ctrlQ))
                host.App.InjectKey(ctrlQ);
        }
    }

    [Fact]
    public async Task Typing_ts_in_the_mnemonic_overlay_toggles_the_sidebar_and_closes()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var sidebarBefore = workbench.IsSidebarVisible;
        var overlayWasGone = false;
        host.App.Iteration += OnOpen;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.NotEqual(sidebarBefore, workbench.IsSidebarVisible);
        Assert.True(overlayWasGone, "MnemonicView did not close after auto-executing");

        void OnOpen(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnOpen;
            if (Key.TryParse("Ctrl+Space", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnTypeT;
        }

        void OnTypeT(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnTypeT;
            if (Key.TryParse("t", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnTypeS;
        }

        void OnTypeS(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnTypeS;
            // After 't' the overlay is still open (ts/tn/tp ambiguous); 's' completes 'ts'.
            if (Key.TryParse("s", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnAssert;
        }

        void OnAssert(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnAssert;
            overlayWasGone = !workbench.SubViews.OfType<TuiCode.Workbench.Mnemonics.MnemonicView>().Any();
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    // Regression for #85: from the leader (and palette) the dialog closes and focus returns to
    // the editor before the command runs, so the old focus-aware ToggleSidebar took its "focus the
    // sidebar" branch and never hid it. The toggle must flip visibility regardless of focus.
    [Fact]
    public async Task Typing_ts_hides_the_sidebar_even_when_the_editor_is_focused()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var fs = new MockFileSystem();
        fs.AddFile("/work/readme.md", new MockFileData("# hi"));
        workbench.Sidebar.Explorer.Open(fs.DirectoryInfo.New("/work"));

        var sidebarHidden = false;
        var explorerHadFocusBeforeToggle = false;
        host.App.Iteration += OnOpen;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.False(explorerHadFocusBeforeToggle, "Test precondition: editor, not explorer, must be focused");
        Assert.True(workbench.IsSidebarVisible == false && sidebarHidden, "Sidebar was not hidden by 'ts'");

        void OnOpen(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnOpen;
            // Move focus into the editor body so the explorer no longer has focus.
            workbench.OpenFile(fs.FileInfo.New("/work/readme.md"));
            explorerHadFocusBeforeToggle = workbench.Sidebar.Explorer.HasFocus;
            if (Key.TryParse("Ctrl+Space", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnTypeT;
        }

        void OnTypeT(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnTypeT;
            if (Key.TryParse("t", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnTypeS;
        }

        void OnTypeS(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnTypeS;
            if (Key.TryParse("s", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnAssert;
        }

        void OnAssert(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnAssert;
            sidebarHidden = !workbench.IsSidebarVisible;
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    // #85 follow-up: the toggle settles focus against the new visibility — a freshly shown sidebar
    // takes focus; a freshly hidden one that held focus hands it back to the editor so focus never
    // sits on an invisible view.
    [Fact]
    public async Task Toggle_focuses_the_sidebar_when_shown_and_the_editor_when_hidden()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        var fs = new MockFileSystem();
        fs.AddFile("/work/readme.md", new MockFileData("# hi"));
        workbench.Sidebar.Explorer.Open(fs.DirectoryInfo.New("/work"));

        var explorerFocusedWhileSidebarUp = false;
        var explorerLostFocusOnHide = false;
        var explorerRefocusedOnShow = false;
        host.App.Iteration += OnStart;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(explorerFocusedWhileSidebarUp, "Test precondition: explorer must hold focus before the toggle");
        Assert.True(explorerLostFocusOnHide, "Hiding a focused sidebar should hand focus to the editor");
        Assert.True(explorerRefocusedOnShow, "Showing the sidebar should move focus to the explorer");

        void OnStart(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnStart;

            // An open tab gives focus somewhere to land when the sidebar is hidden.
            workbench.OpenFile(fs.FileInfo.New("/work/readme.md"));
            workbench.Sidebar.Explorer.SetFocus();
            explorerFocusedWhileSidebarUp = workbench.Sidebar.Explorer.HasFocus;

            commands.TryExecute(CommandIds.ToggleSidebar);   // visible -> hidden
            explorerLostFocusOnHide = !workbench.IsSidebarVisible && !workbench.Sidebar.Explorer.HasFocus;

            commands.TryExecute(CommandIds.ToggleSidebar);   // hidden -> visible
            explorerRefocusedOnShow = workbench.IsSidebarVisible && workbench.Sidebar.Explorer.HasFocus;

            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    // The Win32 console delivers Ctrl+Enter as Ctrl + LineFeed (0x4000000A) rather than Ctrl + Enter
    // (0x4000000D); the native WindowsDriver passes that control char through verbatim, so the
    // settings dialog's "Ctrl+Enter: Save" binding was dead on Windows. WorkbenchHost rewrites the
    // chord back to Ctrl+Enter — and because it does so before dispatching to the scope stack, the
    // rewrite reaches the dialog's pushed modal scope, not just the workbench scope.
    [Fact]
    public async Task Windows_ctrl_linefeed_saves_and_closes_the_settings_dialog()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(
            workbench, commands, keybindings, scopes, settings,
            environment: new FakeEnvironment().SetIsWindows(true),
            driverName: DriverRegistry.Names.ANSI);

        var settingsClosed = false;
        host.App.Iteration += OnOpen;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.True(settingsClosed, "Ctrl+LineFeed did not save+close the settings dialog on Windows");

        void OnOpen(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnOpen;
            if (Key.TryParse("Ctrl+,", out var k)) host.App.InjectKey(k);
            host.App.Iteration += OnSave;
        }

        void OnSave(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnSave;
            // Settings is up; the dialog's modal scope owns Ctrl+Enter. Deliver it the way the
            // Win32 driver would — as Ctrl + LineFeed — and the normalization should still reach it.
            host.App.InjectKey(new Key(KeyCode.CtrlMask | (KeyCode)0x0A));
            host.App.Iteration += OnAssert;
        }

        void OnAssert(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnAssert;
            settingsClosed = !workbench.SubViews.OfType<TuiCode.Workbench.Settings.SettingsView>().Any();
            if (Key.TryParse("Ctrl+Q", out var q)) host.App.InjectKey(q);
        }
    }

    // The rewrite is a Windows-driver workaround; off Windows Ctrl+Enter already arrives as CR, so a
    // bare Ctrl+LineFeed must NOT be mistaken for it (it'd be a genuine Ctrl+J-family key there).
    [Fact]
    public async Task Non_windows_ctrl_linefeed_does_not_fire_the_ctrl_enter_binding()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(
            workbench, commands, keybindings, scopes, settings,
            environment: new FakeEnvironment().SetIsWindows(false),
            driverName: DriverRegistry.Names.ANSI);

        var fired = false;
        commands.Register("test.ctrlEnter", () => fired = true);
        keybindings.Bind("Ctrl+Enter", "test.ctrlEnter");

        host.App.Iteration += OnFirst;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.False(fired, "Ctrl+LineFeed was wrongly normalized to Ctrl+Enter off Windows");

        void OnFirst(object? s, EventArgs<IApplication?> e)
        {
            host.App.Iteration -= OnFirst;
            host.App.InjectKey(new Key(KeyCode.CtrlMask | (KeyCode)0x0A));
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
