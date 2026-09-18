namespace TuiCode.Abstractions;

public enum FileIconStyle
{
    /// <summary><see cref="NerdFont"/> when the terminal looks able to draw it, else <see cref="Emoji"/>.</summary>
    Auto,

    /// <summary>A coloured icon per file type. Needs a Nerd Font, or a terminal that bundles its symbols.</summary>
    NerdFont,

    Emoji,

    Off,
}
