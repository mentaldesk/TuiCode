namespace TuiCode.Editor;

/// <summary>A dirty tab's text and place, kept so it can come back after TuiCode ends unexpectedly (#579), with the file as the tab last saw it.</summary>
public sealed record RecoveredEdits(string Text, ViewPosition Position, FileSnapshot OnDisk);
