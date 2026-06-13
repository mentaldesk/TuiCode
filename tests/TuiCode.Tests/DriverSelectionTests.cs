using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

public class DriverSelectionTests
{
    private static FakeEnvironment NonWindows() => new FakeEnvironment().SetIsWindows(false);

    private static FakeEnvironment Windows() => new FakeEnvironment().SetIsWindows(true);

    [Fact]
    public void Resolve_returns_null_off_windows_when_no_override()
    {
        var result = DriverSelection.Resolve(Array.Empty<string>(), NonWindows());

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_defaults_to_windows_driver_on_windows()
    {
        var result = DriverSelection.Resolve(Array.Empty<string>(), Windows());

        Assert.Equal("windows", result);
    }

    [Fact]
    public void Resolve_reads_driver_flag_with_equals()
    {
        var result = DriverSelection.Resolve(new[] { "--driver=dotnet" }, NonWindows());

        Assert.Equal("dotnet", result);
    }

    [Fact]
    public void Resolve_reads_driver_flag_as_separate_token()
    {
        var result = DriverSelection.Resolve(new[] { "--driver", "dotnet" }, NonWindows());

        Assert.Equal("dotnet", result);
    }

    [Fact]
    public void Resolve_falls_back_to_environment_variable()
    {
        var env = NonWindows().Set(DriverSelection.EnvironmentVariable, "dotnet");

        var result = DriverSelection.Resolve(Array.Empty<string>(), env);

        Assert.Equal("dotnet", result);
    }

    [Fact]
    public void Resolve_prefers_flag_over_environment_variable()
    {
        var env = NonWindows().Set(DriverSelection.EnvironmentVariable, "dotnet");

        var result = DriverSelection.Resolve(new[] { "--driver=ansi" }, env);

        Assert.Equal("ansi", result);
    }

    [Fact]
    public void Resolve_override_wins_over_windows_default()
    {
        var result = DriverSelection.Resolve(new[] { "--driver=ansi" }, Windows());

        Assert.Equal("ansi", result);
    }

    [Fact]
    public void Resolve_trims_surrounding_whitespace()
    {
        var result = DriverSelection.Resolve(new[] { "--driver", "  dotnet  " }, NonWindows());

        Assert.Equal("dotnet", result);
    }

    [Fact]
    public void Resolve_blank_override_falls_through_to_windows_default()
    {
        var env = Windows().Set(DriverSelection.EnvironmentVariable, "   ");

        var result = DriverSelection.Resolve(new[] { "--driver", "   " }, env);

        Assert.Equal("windows", result);
    }

    [Fact]
    public void Resolve_blank_override_off_windows_is_null()
    {
        var env = NonWindows().Set(DriverSelection.EnvironmentVariable, "   ");

        var result = DriverSelection.Resolve(new[] { "--driver", "   " }, env);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_ignores_dangling_flag_with_no_value()
    {
        var result = DriverSelection.Resolve(new[] { "--driver" }, NonWindows());

        Assert.Null(result);
    }
}
