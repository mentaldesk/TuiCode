using System.Text;
using Terminal.Gui.Views;

namespace TuiCode.Abstractions;

/// <summary>A <see cref="Label"/> that shows its text as written: a '_' is drawn, not taken as a hotkey marker (#549).</summary>
public class VerbatimLabel : Label
{
    public static readonly Rune NoHotKey = (Rune)0xffff;

    public VerbatimLabel() => HotKeySpecifier = NoHotKey;
}
