using Microsoft.Extensions.DependencyInjection;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;

var services = new ServiceCollection();

services.AddTransient<SidebarPart>();
services.AddTransient<EditorPart>();
services.AddTransient<StatusBarPart>();
services.AddTransient<Workbench>();
services.AddTransient<WorkbenchHost>();
services.AddSingleton<App>();

using var provider = services.BuildServiceProvider();
provider.GetRequiredService<App>().Run();
