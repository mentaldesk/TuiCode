using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Terminal.Gui.App;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Icons;
using TuiCode.Search;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Icons;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.TerminalIntegration;
using TuiCode.Workbench.Workspace;

if (args.Contains("--smoke-syntax"))
    return SyntaxSmoke.Run(Console.Out);

var services = new ServiceCollection();

// No provider is registered yet — ILogger output is captured through the abstraction but not
// surfaced anywhere. Deciding the sink (file / status bar / diagnostics view) is tracked in #92.
services.AddLogging();

services.AddSingleton<IFileSystem>(_ => new FileSystem());
services.AddSingleton<ICommandService, CommandService>();
services.AddSingleton<IKeybindingService, KeybindingService>();
services.AddSingleton<IInputScopeStack, InputScopeStack>();
services.AddSingleton<ISettingsService, DefaultSettingsService>();
services.AddSingleton<IEnvironment, SystemEnvironment>();
services.AddSingleton<IGitCli>(sp => new GitCli(sp.GetRequiredService<IFileSystem>()));
services.AddSingleton<IGitHubCli>(_ => new GitHubCli());
services.AddSingleton(sp =>
{
    var fs = sp.GetRequiredService<IFileSystem>();
    var userGrammars = fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tui", "grammars");
    return new SyntaxHighlighter(GrammarBundle.Load(fs, userGrammars))
    {
        Associations = sp.GetRequiredService<ISettingsService>().GrammarAssociations,
    };
});
services.AddSingleton(sp => new FileIcons(() => TerminalFontDetection.Detect(
    sp.GetRequiredService<IEnvironment>(), sp.GetRequiredService<IFileSystem>()))
{
    Setting = sp.GetRequiredService<ISettingsService>().FileIcons,
});
services.AddSingleton<ITerminalIntegration, Iterm2Integration>();
services.AddSingleton<ITerminalIntegration, WezTermIntegration>();

services.AddTransient<FileExplorerView>();
services.AddTransient<SearchView>();
services.AddTransient<ReviewView>();
services.AddTransient<SidebarPart>();
services.AddTransient<EditorPart>();
services.AddTransient<StatusBarPart>();
services.AddSingleton(sp => WorkspaceStateStore.ForUser(sp.GetRequiredService<IFileSystem>()));
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
    driverName: DriverSelection.Resolve(args, sp.GetRequiredService<IEnvironment>()),
    logger: sp.GetRequiredService<ILogger<WorkbenchHost>>(),
    icons: sp.GetRequiredService<FileIcons>(),
    git: sp.GetRequiredService<IGitCli>(),
    gitHub: sp.GetRequiredService<IGitHubCli>(),
    fileSystem: sp.GetRequiredService<IFileSystem>()));
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

// Resolve `tuicode <path>` before Application.Init, so the create prompt and any error land on a
// terminal nothing has drawn on yet (#263).
var fileSystem = provider.GetRequiredService<IFileSystem>();
var startup = StartupArguments.Resolve(
    args,
    fileSystem,
    Environment.CurrentDirectory,
    (path, directory) => CreatePrompt.Ask(Console.In, Console.Out, path, directory));
if (startup.Error is { } startupError)
{
    Console.Error.WriteLine(startupError);
    return 1;
}
if (startup.Declined)
    return 0;

// Load persisted settings before resolving App — App's construction triggers
// Application.Init() which reads ThemeManager.Theme for the first paint.
provider.GetRequiredService<ISettingsService>().Load();

using var app = provider.GetRequiredService<App>();
app.Host.Workbench.OpenStartupTarget(startup);

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
