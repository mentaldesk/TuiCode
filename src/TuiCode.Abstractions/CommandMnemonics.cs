namespace TuiCode.Abstractions;

/// <summary>
/// The hard-coded mnemonic for each command, surfaced by the mnemonic leader dialog
/// (issue #50). Unlike keybindings these are <b>not</b> user-configurable — only the
/// leader key that opens the dialog is. They are fixed design decisions so the dialog
/// can show stable hints and so muscle memory carries across machines.
///
/// Invariant: no complete mnemonic is a prefix of another. That lets the dialog
/// auto-execute the instant the typed sequence uniquely identifies a command, with no
/// terminating Enter. Mnemonics therefore come in families under a shared first key
/// (<c>c</c>lose, <c>o</c>pen, <c>f</c>ocus, <c>t</c>ab/toggle, …) that have room to
/// grow: <c>cf</c> close file leaves <c>co</c>/<c>ca</c>/<c>cs</c> free for future
/// close-other/all/sidebar commands. <c>q</c> (quit) and <c>?</c> (help) are the only
/// single-key mnemonics — common enough to be exceptions and unlikely to spawn a family.
/// </summary>
public static class CommandMnemonics
{
    private static readonly IReadOnlyDictionary<string, string> Map = Build();

    /// <summary>The mnemonic for <paramref name="commandId"/>, or null if it has none.</summary>
    public static string? For(string commandId) =>
        Map.TryGetValue(commandId, out var mnemonic) ? mnemonic : null;

    /// <summary>All (command id, mnemonic) pairs that have a mnemonic.</summary>
    public static IEnumerable<KeyValuePair<string, string>> All => Map;

    private static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CommandIds.Quit] = "q",
            [CommandIds.ShowHelp] = "?",
            [CommandIds.SaveActiveEditor] = "sf",
            [CommandIds.ShowDiagnostics] = "sd",
            [CommandIds.New] = "nf",
            [CommandIds.NextEditor] = "tn",
            [CommandIds.PreviousEditor] = "tp",
            [CommandIds.Open] = "of",
            [CommandIds.OpenSettings] = "os",
            [CommandIds.CloseActiveEditor] = "cf",
            [CommandIds.ToggleSidebar] = "ts",
            [CommandIds.FocusEditorBody] = "fe",
            [CommandIds.FocusEditorTabStrip] = "ft",
            [CommandIds.GoToLine] = "gl",
        };

        // f1..f9 mirror the Ctrl+D1..Ctrl+D9 "focus editor tab N" bindings. Digits don't
        // collide with fe/ft, so the f-family stays prefix-free.
        for (var i = 1; i <= 9; i++)
            map[CommandIds.FocusEditorByIndex(i)] = $"f{i}";

        return map;
    }
}
