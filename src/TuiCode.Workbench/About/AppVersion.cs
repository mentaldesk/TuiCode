using System.Reflection;

namespace TuiCode.Workbench.About;

public static class AppVersion
{
    /// <summary>The version About and <c>tuicode --version</c> show.</summary>
    public static string Current =>
        Text(Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);

    /// <summary>MinVer's version, without the commit it appends (#214).</summary>
    public static string Text(string? informationalVersion) =>
        informationalVersion?.Split('+')[0] ?? "unknown";
}
