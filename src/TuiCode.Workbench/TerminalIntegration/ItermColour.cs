using System.Globalization;

namespace TuiCode.Workbench.TerminalIntegration;

/// <summary>A <c>#RRGGBB</c> colour as an iTerm2 profile stores it: sRGB components from 0 to 1.</summary>
internal readonly record struct ItermColour(double Red, double Green, double Blue)
{
    /// <summary>Null unless <paramref name="hex"/> is six hex digits, with or without a leading <c>#</c>.</summary>
    public static ItermColour? Parse(string? hex)
    {
        var digits = hex?.StartsWith('#') == true ? hex[1..] : hex;
        if (digits is not { Length: 6 } || !int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            return null;
        return new ItermColour(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
    }
}
