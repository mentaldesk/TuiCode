using System.Text;
using Terminal.Gui.Drawing;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class ThemeServiceTests
{
    [Fact]
    public void Embedded_dark_and_light_themes_are_discoverable()
    {
        var service = new ThemeService();
        Assert.Contains("dark", service.AvailableThemes);
        Assert.Contains("light", service.AvailableThemes);
    }

    [Fact]
    public void LoadTheme_populates_tokens_so_GetColor_returns_the_parsed_color()
    {
        var service = new ThemeService();
        service.LoadTheme("dark");

        var bg = service.GetColor(ThemeTokens.EditorBackground);
        Assert.Equal("dark", service.CurrentTheme);
        // dark editor.background is #1e1e1e
        Color.TryParse("#1e1e1e", out var expected);
        Assert.Equal(expected!.Value, bg);
    }

    [Fact]
    public void GetColor_throws_for_unmapped_tokens()
    {
        var service = new ThemeService();
        service.LoadTheme("dark");
        Assert.Throws<KeyNotFoundException>(() => service.GetColor("editor.bogus"));
    }

    [Fact]
    public void LoadTheme_fires_ThemeChanged()
    {
        var service = new ThemeService();
        var fired = 0;
        service.ThemeChanged += (_, _) => fired++;

        service.LoadTheme("dark");
        service.LoadTheme("light");

        Assert.Equal(2, fired);
    }

    [Fact]
    public void LoadTheme_throws_for_unknown_theme()
    {
        var service = new ThemeService();
        Assert.Throws<ArgumentException>(() => service.LoadTheme("nonexistent"));
    }

    [Fact]
    public void LoadTheme_throws_when_a_color_value_is_unparseable()
    {
        var service = ServiceWithJson(@"{ ""editor.background"": ""not-a-color"" }");
        Assert.Throws<InvalidOperationException>(() => service.LoadTheme("test"));
    }

    private static ThemeService ServiceWithJson(string json) =>
        new(_ => new MemoryStream(Encoding.UTF8.GetBytes(json)), new[] { "test" });
}
