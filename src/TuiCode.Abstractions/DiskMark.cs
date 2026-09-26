namespace TuiCode.Abstractions;

/// <summary>
/// The mark on a file whose copy on disk has moved out from under its tab (#268, #271), in the tab strip
/// and the status line. A Nerd Font draws nf-oct-alert and nf-fa-ban; every other icon style falls back to
/// the plain warning sign and circled slash.
/// </summary>
public static class DiskMark
{
    /// <summary>Empty for <see cref="DiskState.Unchanged"/>: there's nothing to say about a file that hasn't moved.</summary>
    public static string For(DiskState state, FileIconStyle style) => state switch
    {
        DiskState.Changed => style == FileIconStyle.NerdFont ? "" : "⚠",
        DiskState.Gone => style == FileIconStyle.NerdFont ? "" : "⊘",
        _ => "",
    };
}
