using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

public sealed class SystemEnvironment : IEnvironment
{
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string GetFolderPath(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
}
