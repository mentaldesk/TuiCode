using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Terminal.Gui.App;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.TerminalIntegration;

var services = new ServiceCollection();

services.AddSingleton<IFileSystem>(_ => new FileSystem());
services.AddSingleton<ICommandService, CommandService>();
services.AddSingleton<IKeybindingService, KeybindingService>();
services.AddSingleton<IInputScopeStack, InputScopeStack>();
services.AddSingleton<ISettingsService, DefaultSettingsService>();
services.AddSingleton<IEnvironment, SystemEnvironment>();
services.AddSingleton<ITerminalIntegration, Iterm2Integration>();
services.AddSingleton<ITerminalIntegration, WezTermIntegration>();

services.AddTransient<FileExplorerView>();
services.AddTransient<SidebarPart>();
services.AddTransient<EditorPart>();
services.AddTransient<StatusBarPart>();
services.AddTransient<Workbench>();
// Driver override (--driver <name> / TUICODE_DRIVER) lets us A/B the TG driver on Windows,
// where the auto-selected `ansi` driver mis-decodes kitty key events (issue #82). Resolved
// per-construction so it picks up the same IEnvironment the rest of the app uses.
services.AddTransient<WorkbenchHost>(sp => new WorkbenchHost(
    sp.GetRequiredService<Workbench>(),
    sp.GetRequiredService<ICommandService>(),
    sp.GetRequiredService<IKeybindingService>(),
    sp.GetRequiredService<IInputScopeStack>(),
    sp.GetRequiredService<ISettingsService>(),
    sp.GetRequiredService<IEnumerable<ITerminalIntegration>>(),
    sp.GetRequiredService<IEnvironment>(),
    timeProvider: null,
    driverName: DriverSelection.Resolve(args, sp.GetRequiredService<IEnvironment>())));
services.AddSingleton<App>();

using var provider = services.BuildServiceProvider();

// Terminal-integration CLI: handles --install/--uninstall/--list/--check flags
// and exits without booting the TUI. Returns null when no flag matched.
var cli = new TerminalIntegrationCli(
    provider.GetRequiredService<IEnumerable<ITerminalIntegration>>(),
    Console.Out);
var cliExit = cli.TryHandle(args);
if (cliExit is int code)
    return code;

// Load persisted settings before resolving App — App's construction triggers
// Application.Init() which reads ThemeManager.Theme for the first paint.
provider.GetRequiredService<ISettingsService>().Load();

using var app = provider.GetRequiredService<App>();
var fileSystem = provider.GetRequiredService<IFileSystem>();
app.Host.Workbench.Sidebar.Explorer.Open(
    fileSystem.DirectoryInfo.New(Environment.CurrentDirectory));

// --smoke: boot through Application.Init + one render iteration, then quit.
// CI runs this against the AOT-published binary to catch runtime failures
// (missing metadata, trim-stripped paths) that publish-time analyzers don't.
if (args.Contains("--smoke"))
{
    void QuitOnFirstIteration(object? sender, EventArgs<IApplication?> e)
    {
        app.Host.App.Iteration -= QuitOnFirstIteration;
        app.Host.App.RequestStop();
    }
    app.Host.App.Iteration += QuitOnFirstIteration;
}

app.Run();
return 0;
