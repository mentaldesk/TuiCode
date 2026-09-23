using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Syntax;

namespace TuiCode.Workbench.Navigation;

/// <summary>The outline's kind icons (<c>nf-cod-symbol_*</c>), Nerd Font only: no emoji reads as "interface".</summary>
internal static class SymbolIcons
{
    public static string? For(SymbolKind kind, FileIconStyle style) =>
        style == FileIconStyle.NerdFont ? Glyph(kind) : null;

    /// <summary>The columns a row keeps for its icon and the space after it; 0 with icons off.</summary>
    public static int Width(FileIconStyle style) =>
        style == FileIconStyle.NerdFont ? Enum.GetValues<SymbolKind>().Max(k => Glyph(k).GetColumns()) + 1 : 0;

    private static string Glyph(SymbolKind kind) => kind switch
    {
        SymbolKind.Class => "",
        SymbolKind.Interface or SymbolKind.Trait => "",
        SymbolKind.Enum => "",
        SymbolKind.Struct => "",
        SymbolKind.Method => "",
        SymbolKind.Field => "",
        SymbolKind.EnumMember => "",
        SymbolKind.Heading => "",
        _ => "",
    };
}
