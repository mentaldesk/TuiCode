using Terminal.Gui.Drawing;

namespace TuiCode.Abstractions;

public interface IThemeService
{
    /// <summary>The name of the currently loaded theme (e.g. "dark", "light").</summary>
    string CurrentTheme { get; }

    /// <summary>Names of all themes available to <see cref="LoadTheme"/>.</summary>
    IReadOnlyCollection<string> AvailableThemes { get; }

    /// <summary>
    /// Resolve a semantic colour token (e.g. "editor.background"). Throws if the token is
    /// not defined by the current theme — unmapped tokens are bugs, not silent fallbacks.
    /// </summary>
    Color GetColor(string token);

    /// <summary>Load the named theme and rebuild registered schemes. Fires <see cref="ThemeChanged"/>.</summary>
    void LoadTheme(string name);

    /// <summary>Fired after a theme finishes loading and schemes have been re-registered.</summary>
    event EventHandler? ThemeChanged;
}
