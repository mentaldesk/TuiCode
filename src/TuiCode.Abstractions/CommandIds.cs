namespace TuiCode.Abstractions;

public static class CommandIds
{
    public const string Quit = "workbench.action.quit";
    public const string SaveActiveEditor = "workbench.action.saveActiveEditor";
    public const string CloseActiveEditor = "workbench.action.closeActiveEditor";
    public const string NextEditor = "workbench.action.nextEditor";
    public const string PreviousEditor = "workbench.action.previousEditor";

    public const string ToggleSidebar = "workbench.action.toggleSidebar";
    public const string FocusSidebar = "workbench.action.focusSidebar";
    public const string FocusEditorBody = "workbench.action.focusEditorBody";
    public const string FocusEditorTabStrip = "workbench.action.focusEditorTabStrip";
    public const string FocusReview = "workbench.action.focusReview";
    public const string ToggleGutter = "workbench.action.toggleGutter";
    public const string OpenSettings = "workbench.action.openSettings";
    public const string Open = "workbench.action.open";
    public const string New = "workbench.action.new";

    public const string SettingsSave = "settings.action.save";
    public const string SettingsCancel = "settings.action.cancel";
    public const string SettingsFocusCategories = "settings.action.focusCategories";

    public const string ShowActions = "workbench.action.showActions";
    public const string ShowMnemonics = "workbench.action.showMnemonics";
    public const string ShowHelp = "workbench.action.showHelp";
    public const string ShowDiagnostics = "workbench.action.showDiagnostics";
    public const string ShowAbout = "workbench.action.showAbout";
    public const string ShowDocumentInfo = "workbench.action.showDocumentInfo";
    public const string CompareToSaved = "workbench.action.compareToSaved";
    public const string CompareToRevision = "workbench.action.compareToRevision";
    public const string CompareToOtherFile = "workbench.action.compareToOtherFile";

    public const string NextChange = "diff.action.nextChange";
    public const string PreviousChange = "diff.action.previousChange";
    public const string GoToChangeLine = "diff.action.goToLine";
    public const string ScrollDiffLeft = "diff.action.scrollLeft";
    public const string ScrollDiffRight = "diff.action.scrollRight";
    public const string ScrollDiffPageLeft = "diff.action.scrollPageLeft";
    public const string ScrollDiffPageRight = "diff.action.scrollPageRight";

    public const string HelpClose = "help.action.close";
    public const string DiagnosticsClose = "diagnostics.action.close";
    public const string AboutClose = "about.action.close";
    public const string DocumentInfoClose = "documentInfo.action.close";

    public const string ActionsExecute = "actions.action.execute";
    public const string ActionsCancel = "actions.action.cancel";
    public const string ActionsFocusList = "actions.action.focusList";
    public const string ActionsFocusSearch = "actions.action.focusSearch";

    public const string GoToLine = "workbench.action.goToLine";
    public const string GoToLineConfirm = "goToLine.action.confirm";
    public const string GoToLineCancel = "goToLine.action.cancel";

    public const string NavigateBack = "workbench.action.navigateBack";
    public const string NavigateForward = "workbench.action.navigateForward";

    public const string FindInFile = "workbench.action.findInFile";
    public const string ReplaceInFile = "workbench.action.replaceInFile";
    public const string FindNext = "find.action.next";
    public const string FindPrevious = "find.action.previous";
    public const string FindClose = "find.action.close";
    public const string FindSwitchField = "find.action.switchField";
    public const string FindReplaceAll = "find.action.replaceAll";

    public const string ShowExplorer = "workbench.action.showExplorer";
    public const string FindGlobally = "workbench.action.findGlobally";
    public const string ReplaceGlobally = "workbench.action.replaceGlobally";
    public const string SearchFocusResults = "search.action.focusResults";
    public const string SearchSwitchField = "search.action.switchField";
    public const string SearchReplaceAll = "search.action.replaceAll";

    public const string OpenConfirm = "open.action.confirm";
    public const string OpenCancel = "open.action.cancel";

    public const string RevisionPickerConfirm = "revisionPicker.action.confirm";
    public const string RevisionPickerCancel = "revisionPicker.action.cancel";
    public const string RevisionPickerUp = "revisionPicker.action.up";
    public const string RevisionPickerDown = "revisionPicker.action.down";
    public const string RevisionPickerPageUp = "revisionPicker.action.pageUp";
    public const string RevisionPickerPageDown = "revisionPicker.action.pageDown";

    public const string PathPromptConfirm = "pathPrompt.action.confirm";
    public const string PathPromptCancel = "pathPrompt.action.cancel";
    public const string PathPromptCycleSelection = "pathPrompt.action.cycleSelection";

    public const string DeleteFile = "explorer.action.deleteFile";
    public const string RenameFile = "explorer.action.renameFile";
    public const string CutFile = "explorer.action.cutFile";
    public const string PasteFile = "explorer.action.pasteFile";
    public const string CancelCut = "explorer.action.cancelCut";
    public const string ConfirmCancel = "confirm.action.cancel";

    public const string MoveLinesUp = "editor.action.moveLinesUp";
    public const string MoveLinesDown = "editor.action.moveLinesDown";
    public const string DuplicateLinesUp = "editor.action.duplicateLinesUp";
    public const string DuplicateLinesDown = "editor.action.duplicateLinesDown";
    public const string AddCursorAbove = "editor.action.addCursorAbove";
    public const string AddCursorBelow = "editor.action.addCursorBelow";
    public const string SelectNextOccurrence = "editor.action.addSelectionToNextFindMatch";
    public const string SelectPreviousOccurrence = "editor.action.addSelectionToPreviousFindMatch";
    public const string SelectAllOccurrences = "editor.action.selectHighlights";
    public const string RemoveSecondaryCursors = "editor.action.removeSecondaryCursors";
    public const string ToggleColumnSelect = "editor.action.toggleColumnSelection";

    public const string ChangeGrammar = "workbench.action.changeGrammar";
    public const string GrammarPickerConfirm = "grammarPicker.action.confirm";
    public const string GrammarPickerCancel = "grammarPicker.action.cancel";

    public static string FocusEditorByIndex(int oneBasedIndex) =>
        $"workbench.action.focusEditor{oneBasedIndex}";
}
