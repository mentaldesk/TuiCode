using System.Text.Json;
using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

public class DefaultSettingsServiceTests
{
    [Fact]
    public void Save_writes_empty_object_when_nothing_differs_from_defaults()
    {
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
        var fs = new MockFileSystem();
        // Pre-populate config in TG's native format so LoadTheme picks it up at construction.
        WriteConfig(fs, """{"Theme":"Dark"}""");
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var json = fs.File.ReadAllText(ConfigPath(fs));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Dark", doc.RootElement.GetProperty("Theme").GetString());
    }

    [Fact]
    public void Save_creates_parent_directory_if_missing()
    {
        var fs = new MockFileSystem();
        WriteConfig(fs, """{"Theme":"Dark"}""");
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var dir = fs.Path.GetDirectoryName(ConfigPath(fs));
        Assert.True(fs.Directory.Exists(dir));
    }

    [Fact]
    public void LoadTheme_reads_TG_native_format_from_config_file()
    {
        var fs = new MockFileSystem();
        WriteConfig(fs, """{"Theme":"Light"}""");
        var svc = new DefaultSettingsService(fs);

        Assert.Equal("Light", svc.Theme);
    }

    [Fact]
    public void LoadTheme_returns_default_when_config_file_is_absent()
    {
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        Assert.Equal("Default", svc.Theme);
    }

    [Fact]
    public void LoadTheme_returns_default_when_config_file_is_malformed()
    {
        var fs = new MockFileSystem();
        WriteConfig(fs, "not valid json {{");
        var svc = new DefaultSettingsService(fs);

        Assert.Equal("Default", svc.Theme);
    }

    private static void WriteConfig(MockFileSystem fs, string content)
    {
        var path = ConfigPath(fs);
        fs.AddDirectory(fs.Path.GetDirectoryName(path)!);
        fs.AddFile(path, new MockFileData(content));
    }

    private static string ConfigPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.config.json");
    }
}
