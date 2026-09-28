using System.Globalization;
using TuiCode.Abstractions;

namespace TuiCode.Icons;

/// <summary>A glyph and, where it has any, its colours as <c>0xRRGGBB</c> for dark and light backgrounds.</summary>
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

    // nf-cod-diff_added, _modified, _removed and _renamed.
    private static readonly Dictionary<GitChangeKind, (string Glyph, string Letter, string ThemeKey, int Dark, int Light)> Changes = new()
    {
        [GitChangeKind.Added] = ("\ueadc", "A", "gitDecoration.addedResourceForeground", 0x81b88b, 0x587c0c),
        [GitChangeKind.Modified] = ("\ueade", "M", "gitDecoration.modifiedResourceForeground", 0xe2c08d, 0x895503),
        [GitChangeKind.Deleted] = ("\ueadf", "D", "gitDecoration.deletedResourceForeground", 0xc74e39, 0xad0707),
        [GitChangeKind.Renamed] = ("\ueae0", "R", "gitDecoration.renamedResourceForeground", 0x73a5e6, 0x0451a5),
    };

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

    /// <summary>
    /// What a branch did to a file (#320), coloured from <paramref name="themeColors"/> where the theme sets its
    /// <c>gitDecoration.*</c> key; null with icons off.
    /// </summary>
    public FileIcon? ForChange(GitChangeKind kind, IReadOnlyDictionary<string, string>? themeColors = null)
    {
        var (glyph, letter, key, dark, light) = Changes[kind];
        if (themeColors?.GetValueOrDefault(key) is { } hex && ParseRgb(hex) is { } themed) dark = light = themed;
        return Style switch
        {
            FileIconStyle.NerdFont => new FileIcon(glyph, dark, light),
            FileIconStyle.Emoji => new FileIcon(letter, dark, light),
            _ => null,
        };
    }

    private static int? ParseRgb(string hex) =>
        hex.Length is 7 or 9 && hex[0] == '#' && int.TryParse(hex.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? rgb
            : null;
}

public sealed record FontDetection(bool NerdFont, string Reason);
