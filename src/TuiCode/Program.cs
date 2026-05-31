using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Terminal.Gui.App;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

var services = new ServiceCollection();

services.AddSingleton<IFileSystem>(_ => new FileSystem());
services.AddSingleton<ICommandService, CommandService>();
services.AddSingleton<IKeybindingService, KeybindingService>();
services.AddSingleton<IInputScopeStack, InputScopeStack>();
services.AddSingleton<ISettingsService, DefaultSettingsService>();

services.AddTransient<FileExplorerView>();
services.AddTransient<SidebarPart>();
services.AddTransient<EditorPart>();
services.AddTransient<StatusBarPart>();
services.AddTransient<Workbench>();
services.AddTransient<WorkbenchHost>();
services.AddSingleton<App>();

using var provider = services.BuildServiceProvider();

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
