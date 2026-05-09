namespace TuiCode.Workbench.Services;

/// <summary>
/// Well-known theme token names. Centralized so that views and themes agree on spelling,
/// and adding a new view-relevant token surfaces a single editing point.
/// </summary>
public static class ThemeTokens
{
    public const string EditorBackground = "editor.background";
    public const string EditorForeground = "editor.foreground";

    public const string SideBarBackground = "sideBar.background";
    public const string SideBarForeground = "sideBar.foreground";

    public const string StatusBarBackground = "statusBar.background";
    public const string StatusBarForeground = "statusBar.foreground";

    public const string TabActiveBackground = "tab.activeBackground";
    public const string TabActiveForeground = "tab.activeForeground";
    public const string TabInactiveBackground = "tab.inactiveBackground";
    public const string TabInactiveForeground = "tab.inactiveForeground";

    public const string ListFocusBackground = "list.focusBackground";
    public const string ListFocusForeground = "list.focusForeground";

    public const string FocusBorder = "focusBorder";
}

/// <summary>
/// Well-known scheme names registered with TG's <c>SchemeManager</c>. Views set their
/// <c>SchemeName</c> property to one of these so theme reloads take effect automatically.
/// </summary>
public static class SchemeNames
{
    public const string Editor = "tuicode.editor";
    public const string Sidebar = "tuicode.sidebar";
    public const string StatusBar = "tuicode.statusBar";
    public const string Tabs = "tuicode.tabs";
}
