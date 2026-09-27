using System.Text.Json;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.TerminalIntegration;
using TuiCode.Workbench.Themes;

namespace TuiCode.Tests;

public class CursorColourProfileHostTests : StaticConfigurationTest
{
    private const string Home = "/Users/test";
    private static readonly string ProfilePath =
        Path.Combine(Home, "Library", "Application Support", "iTerm2", "DynamicProfiles", "tuicode.json");

    private readonly CommandService _commands = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly MockFileSystem _fs = new();
    private readonly Iterm2Integration _iterm;

    public CursorColourProfileHostTests()
    {
        _iterm = new Iterm2Integration(_fs, new FakeEnvironment().SetFolder(Environment.SpecialFolder.UserProfile, Home));
        _fs.Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        _fs.File.WriteAllText(ProfilePath, """{ "Profiles": [ { "TuiCodeIntegrationVersion": 2, "Guid": "x" } ] }""");
    }

    [Fact]
    public void Startup_brings_a_profile_from_an_older_TuiCode_up_to_date_with_the_theme_cursor()
    {
        using var host = BuildHost();

        Assert.Equal(TerminalIntegrationStatus.Installed, _iterm.GetStatus());
        Assert.Equal(0xE6 / 255.0, CursorRed());
    }

    [Fact]
    public void Changing_theme_rewrites_the_profile_with_the_new_cursor_colour()
    {
        using var host = BuildHost();

        _settings.Theme = BundledThemes.Daylight;

        Assert.Equal(0x1F / 255.0, CursorRed());
    }

    private double CursorRed()
    {
        using var doc = JsonDocument.Parse(_fs.File.ReadAllText(ProfilePath));
        return doc.RootElement.GetProperty("Profiles")[0].GetProperty("Cursor Color (Light)").GetProperty("Red Component").GetDouble();
    }

    private WorkbenchHost BuildHost()
    {
        var workbench = new Workbench.Workbench(
            new SidebarPart(new FileExplorerView()), new EditorPart(new SyntaxHighlighter(GrammarBundle.Load())), new StatusBarPart());
        return new WorkbenchHost(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(), _settings,
            terminalIntegrations: [_iterm], driverName: DriverRegistry.Names.ANSI);
    }
}
