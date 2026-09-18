using TuiCode.Abstractions;

namespace TuiCode.Icons;

/// <summary>A glyph and, for Nerd Font icons, its colours as <c>0xRRGGBB</c> for dark and light backgrounds.</summary>
public readonly record struct FileIcon(string Glyph, int? DarkColor = null, int? LightColor = null)
{
    public int? ColorFor(bool darkBackground) => darkBackground ? DarkColor : LightColor;
}

public sealed class FileIcons
{
    private static readonly FileIcon NerdFolder = new("\uf07b", 0x519aba, 0x3a7a99);
    private static readonly FileIcon NerdFolderOpen = new("\uf07c", 0x519aba, 0x3a7a99);
    private static readonly FileIcon NerdFile = new("\uf0f6", 0x6d8086, 0x6d8086);
    private static readonly FileIcon EmojiFolder = new("📁");
    private static readonly FileIcon EmojiFolderOpen = new("📂");
    private static readonly FileIcon EmojiFile = new("📄");

    private readonly Lazy<FontDetection> _detection;
    private FileIconStyle _setting;

    /// <param name="detect">Only called once Auto needs it.</param>
    public FileIcons(Func<FontDetection> detect)
    {
        _detection = new Lazy<FontDetection>(detect);
    }

    public FontDetection Detection => _detection.Value;

    public FileIconStyle Setting
    {
        get => _setting;
        set
        {
            if (_setting == value) return;
            _setting = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary><see cref="Setting"/> with Auto resolved.</summary>
    public FileIconStyle Style => _setting == FileIconStyle.Auto
        ? Detection.NerdFont ? FileIconStyle.NerdFont : FileIconStyle.Emoji
        : _setting;

    public event EventHandler? Changed;

    public FileIcon? ForFile(string name) => Style switch
    {
        FileIconStyle.NerdFont => FileIconTable.Lookup(name) ?? NerdFile,
        FileIconStyle.Emoji => EmojiFile,
        _ => null,
    };

    public FileIcon? ForDirectory(bool expanded) => Style switch
    {
        FileIconStyle.NerdFont => expanded ? NerdFolderOpen : NerdFolder,
        FileIconStyle.Emoji => expanded ? EmojiFolderOpen : EmojiFolder,
        _ => null,
    };
}

public sealed record FontDetection(bool NerdFont, string Reason);
