using Terminal.Gui.Configuration;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Touches both TuiCodeSettings.Theme and ThemeManager.Theme (static TG state).
[Collection("StaticConfiguration")]
public class WorkbenchHostThemeTests
{
    [Fact]
    public void Startup_applies_persisted_theme_to_ThemeManager()
    {
        using var _ = new ThemeFixture("Dark");
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings);

        // WorkbenchHost must bridge TuiCodeSettings.Theme → ThemeManager.Theme after Init().
        // ConfigurationManager.Apply() (called during Init) sets them independently; without
        // the explicit assignment in WorkbenchHost the app would always render with Default.
        Assert.Equal(TuiCodeSettings.Theme, ThemeManager.Theme);
    }

    private static Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());

    /// <summary>
    /// Snapshots and restores both <see cref="TuiCodeSettings.Theme"/> and
    /// <see cref="ThemeManager.Theme"/> so theme-mutating tests don't leak into each other.
    /// Any test that constructs <see cref="WorkbenchHost"/> with a non-default theme should
    /// use this fixture — Init() writes ThemeManager, not just TuiCodeSettings.
    /// </summary>
    private sealed class ThemeFixture : IDisposable
    {
        private readonly string _prevSettings;
        private readonly string _prevManager;

        public ThemeFixture(string theme)
        {
            _prevSettings = TuiCodeSettings.Theme;
            _prevManager = ThemeManager.Theme;
            TuiCodeSettings.Theme = theme;
        }

        public void Dispose()
        {
            TuiCodeSettings.Theme = _prevSettings;
            ThemeManager.Theme = _prevManager;
        }
    }
}
