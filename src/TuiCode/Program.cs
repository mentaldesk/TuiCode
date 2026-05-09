using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Terminal.Gui.Configuration;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

// Load TG's configuration hierarchy (library defaults → app resources → ~/.tui/TuiCode.config.json
// → cwd → env → runtime). Among other things, this reads {"Theme": "Dark"} and sets
// ThemeManager.Theme before Application.Init() runs, so the workbench renders with the saved
// theme on first paint.
ConfigurationManager.Enable(ConfigLocations.All);

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

using var app = provider.GetRequiredService<App>();
var fileSystem = provider.GetRequiredService<IFileSystem>();
app.Host.Workbench.Sidebar.Explorer.Open(
    fileSystem.DirectoryInfo.New(Environment.CurrentDirectory));
app.Run();
