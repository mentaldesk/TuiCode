namespace TuiCode.Abstractions;

/// <summary>
/// The mark on a file that changed on disk (#268), in the tab strip and the status line. A Nerd Font
/// draws nf-oct-alert; every other icon style falls back to the plain warning sign.
/// </summary>
public static class WarningMark
{
    public static string For(FileIconStyle style) =>
        style == FileIconStyle.NerdFont ? "\uf421" : "\u26a0";
}
