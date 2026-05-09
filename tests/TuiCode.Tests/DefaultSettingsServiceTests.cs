using System.Text.Json;
using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

// All tests touch the static TuiCodeSettings.Theme. xUnit runs distinct test classes
// in parallel by default; mark this collection so its tests serialize.
[Collection("StaticConfiguration")]
public class DefaultSettingsServiceTests
{
    [Fact]
    public void Save_writes_empty_object_when_nothing_differs_from_defaults()
    {
        using var _ = new ThemeFixture(TuiCodeSettings.DefaultTheme);
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var path = ConfigPath(fs);
        Assert.True(fs.File.Exists(path));
        var json = fs.File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        Assert.Empty(doc.RootElement.EnumerateObject());
    }

    [Fact]
    public void Save_writes_only_the_theme_when_it_differs_from_default()
    {
        using var _ = new ThemeFixture("Dark");
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var json = fs.File.ReadAllText(ConfigPath(fs));
        using var doc = JsonDocument.Parse(json);
        var appSettings = doc.RootElement.GetProperty("AppSettings");
        Assert.Equal("Dark", appSettings.GetProperty("TuiCodeSettings.Theme").GetString());
    }

    [Fact]
    public void Save_creates_parent_directory_if_missing()
    {
        using var _ = new ThemeFixture("Dark");
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var dir = fs.Path.GetDirectoryName(ConfigPath(fs));
        Assert.True(fs.Directory.Exists(dir));
    }

    private static string ConfigPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.config.json");
    }

    /// <summary>Snapshot+restore the static <see cref="TuiCodeSettings.Theme"/> for test isolation.</summary>
    private sealed class ThemeFixture : IDisposable
    {
        private readonly string _previous;
        public ThemeFixture(string theme)
        {
            _previous = TuiCodeSettings.Theme;
            TuiCodeSettings.Theme = theme;
        }
        public void Dispose() => TuiCodeSettings.Theme = _previous;
    }
}
