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

    public static string FocusEditorByIndex(int oneBasedIndex) =>
        $"workbench.action.focusEditor{oneBasedIndex}";
}
