using System.Reflection;
using TuiCode.Workbench;

namespace TuiCode.Tests;

public class AppVersionTests
{
    [Fact]
    public void VersionText_drops_the_commit_MinVer_appends()
    {
        Assert.Equal("0.0.5-alpha.0.3", WorkbenchHost.VersionText("0.0.5-alpha.0.3+81e2e636197fc4f0377c9f7cfe37dbafe9862907"));
        Assert.Equal("0.0.4", WorkbenchHost.VersionText("0.0.4"));
    }

    [Fact]
    public void VersionText_of_an_assembly_without_the_attribute_is_unknown()
    {
        Assert.Equal("unknown", WorkbenchHost.VersionText(null));
    }

    [Fact]
    public void The_shipped_assembly_is_versioned_from_the_tags_not_the_SDK_default()
    {
        var version = typeof(WorkbenchHost).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.NotNull(version);
        Assert.NotEqual("1.0.0", WorkbenchHost.VersionText(version));
    }
}
