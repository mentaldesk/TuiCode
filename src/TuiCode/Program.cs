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
// → cwd → env → runtime). This populates static [ConfigurationProperty]-decorated values such as
// TuiCodeSettings.Theme so they are available when DI builds. WorkbenchHost then maps
// TuiCodeSettings.Theme → ThemeManager.Theme after Application.Init(), because Apply() sets them
// independently and Init() leaves ThemeManager at its default.
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
