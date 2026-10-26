using TuiCode.Workbench.Help;

namespace TuiCode.Tests;

internal static class SidebarHelp
{
    /// <summary>The rows every sidebar tab's F1 column ends with: the keys that show the other tabs.</summary>
    public static readonly KeyRow[] TabKeys =
    [
        new("Ctrl+Shift+E", "Show explorer"),
        new("Ctrl+Shift+F", "Show find"),
        new("Ctrl+Shift+U", "Show usages"),
        new("Ctrl+Shift+R", "Show review"),
        new("Ctrl+PageDown", "Next sidebar tab"),
        new("Ctrl+PageUp", "Previous sidebar tab"),
    ];
}
