using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Abstractions;

/// <summary>
/// The theme's <c>Warning</c> scheme (#268): the one colour a file changed on disk is named in, read by
/// the editor tab strip and the explorer alike. Terminal.Gui has no <c>VisualRole</c> for a warning, so
/// only the foreground is taken — whatever the row or tab header is drawn over stays as it was.
/// </summary>
public static class WarningColour
{
    public const string SchemeName = "Warning";

    /// <summary>Null when no theme is loaded, as in tests that don't need one.</summary>
    public static Color? Foreground =>
        SchemeManager.TryGetScheme(SchemeName, out var scheme) ? scheme.Normal.Foreground : null;

    public static Attribute On(Attribute over) =>
        Foreground is { } colour ? over with { Foreground = colour } : over;
}
