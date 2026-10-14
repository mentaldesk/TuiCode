using TuiCode.Abstractions;

namespace TuiCode.Workbench.Files;

public static class SystemTrash
{
    public static ITrash For(IFileSystem fs, IEnvironment environment)
    {
        if (OperatingSystem.IsWindows()) return new WindowsTrash();
        if (OperatingSystem.IsMacOS()) return new MacTrash();
        return FreedesktopTrash.ForCurrentUser(fs, environment);
    }
}
