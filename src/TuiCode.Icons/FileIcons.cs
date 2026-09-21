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
    // nf-md-chat and nf-md-chat_remove_outline.
    private static readonly FileIcon NerdThreadsOpen = new("\U000F0B79");
    private static readonly FileIcon NerdThreadsSettled = new("\U000F1414");
    private static readonly FileIcon EmojiThreadsOpen = new("💬");
    private static readonly FileIcon EmojiThreadsSettled = new("💭");

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

    /// <summary>The mark on a file with review threads on it (#186), open ones apart from settled ones.</summary>
    public FileIcon? ForThreads(bool unresolved) => Style switch
    {
        FileIconStyle.NerdFont => unresolved ? NerdThreadsOpen : NerdThreadsSettled,
        FileIconStyle.Emoji => unresolved ? EmojiThreadsOpen : EmojiThreadsSettled,
        _ => null,
    };
}

public sealed record FontDetection(bool NerdFont, string Reason);
