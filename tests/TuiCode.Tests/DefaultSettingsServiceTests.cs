using System.Text.Json;
using Terminal.Gui.Configuration;
using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

// Touches ThemeManager.Theme (TG static state). Serialise via the shared collection.
[Collection("StaticConfiguration")]
public class DefaultSettingsServiceTests
{
    [Fact]
    public void Save_writes_empty_object_when_theme_is_default()
    {
        using var _ = new ThemeFixture("Default");
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
    public void Save_writes_theme_in_TG_native_format_when_non_default()
    {
        using var _ = new ThemeFixture("Dark");
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var json = fs.File.ReadAllText(ConfigPath(fs));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Dark", doc.RootElement.GetProperty("Theme").GetString());
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

    /// <summary>
    /// Snapshot+restore <see cref="ThemeManager.Theme"/> for test isolation. The setter
    /// also calls <c>ConfigurationManager.Apply()</c>, so any side effects of activating
    /// the theme are exercised the same way they would be in production.
    /// </summary>
    private sealed class ThemeFixture : IDisposable
    {
        private readonly string _previous;
        public ThemeFixture(string theme)
        {
            _previous = ThemeManager.Theme;
            ThemeManager.Theme = theme;
        }
        public void Dispose() => ThemeManager.Theme = _previous;
    }
}
