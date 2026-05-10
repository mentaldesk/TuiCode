namespace TuiCode.Abstractions;

public static class CommandIds
{
    public const string Quit = "workbench.action.quit";
    public const string SaveActiveEditor = "workbench.action.saveActiveEditor";
    public const string CloseActiveEditor = "workbench.action.closeActiveEditor";
    public const string NextEditor = "workbench.action.nextEditor";
    public const string PreviousEditor = "workbench.action.previousEditor";

    public const string ToggleSidebar = "workbench.action.toggleSidebar";
    public const string FocusEditorBody = "workbench.action.focusEditorBody";
    public const string FocusEditorTabStrip = "workbench.action.focusEditorTabStrip";
    public const string OpenSettings = "workbench.action.openSettings";

    public const string SettingsSave = "settings.action.save";
    public const string SettingsCancel = "settings.action.cancel";
    public const string SettingsFocusCategories = "settings.action.focusCategories";

    public const string ShowActions = "workbench.action.showActions";
    public const string ShowHelp = "workbench.action.showHelp";

    public const string HelpClose = "help.action.close";

    public const string ActionsExecute = "actions.action.execute";
    public const string ActionsCancel = "actions.action.cancel";
    public const string ActionsFocusList = "actions.action.focusList";
    public const string ActionsFocusSearch = "actions.action.focusSearch";

    public const string GoToLine = "workbench.action.goToLine";
    public const string GoToLineConfirm = "goToLine.action.confirm";
    public const string GoToLineCancel = "goToLine.action.cancel";

    public const string NavigateBack = "workbench.action.navigateBack";
    public const string NavigateForward = "workbench.action.navigateForward";

    public static string FocusEditorByIndex(int oneBasedIndex) =>
        $"workbench.action.focusEditor{oneBasedIndex}";
}
