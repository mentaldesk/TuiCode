using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Editor;

/// <summary>Turns token metadata into attributes, keeping the base attribute's background.</summary>
internal sealed class TokenPalette
{
    private Color[] _colors = [];
    private int _themeVersion = -1;

    public void Sync(SyntaxHighlighter highlighter)
    {
        if (_themeVersion == highlighter.ThemeVersion) return;
        _colors = highlighter.Colors.Select(hex => hex is null ? default : Color.Parse(hex)).ToArray();
        _themeVersion = highlighter.ThemeVersion;
    }

    public Attribute Apply(Attribute attribute, int metadata)
    {
        var foreground = SyntaxHighlighter.ForegroundOf(metadata);
        if (foreground != SyntaxHighlighter.DefaultForeground && foreground < _colors.Length)
            attribute = attribute with { Foreground = _colors[foreground] };

        var style = SyntaxHighlighter.StyleOf(metadata);
        if (style == TokenStyle.None) return attribute;
        return attribute with
        {
            Style = attribute.Style
                    | (style.HasFlag(TokenStyle.Italic) ? TextStyle.Italic : TextStyle.None)
                    | (style.HasFlag(TokenStyle.Bold) ? TextStyle.Bold : TextStyle.None)
                    | (style.HasFlag(TokenStyle.Underline) ? TextStyle.Underline : TextStyle.None)
                    | (style.HasFlag(TokenStyle.Strikethrough) ? TextStyle.Strikethrough : TextStyle.None),
        };
    }
}
