using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;

var services = new ServiceCollection();

services.AddSingleton<IFileSystem>(_ => new FileSystem());

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
