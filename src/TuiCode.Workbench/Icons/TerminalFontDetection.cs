using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TuiCode.Abstractions;
using TuiCode.Icons;

namespace TuiCode.Workbench.Icons;

// No terminal reports its font, so this is a best guess; see "File icons" in AGENTS.md.
public static partial class TerminalFontDetection
{
    public static FontDetection Detect(IEnvironment env, IFileSystem fs, Func<string, string?>? plistToXml = null)
    {
        var termProgram = env.GetEnvironmentVariable("TERM_PROGRAM");
        bool Is(string program, string marker) =>
            string.Equals(termProgram, program, StringComparison.OrdinalIgnoreCase)
            // Terminal multiplexers replace TERM_PROGRAM, but these survive.
            || (termProgram is null or "tmux" or "screen" && env.GetEnvironmentVariable(marker) is not null);

        if (string.Equals(termProgram, "vscode", StringComparison.Ordinal))
            return FromFonts("VS Code", VsCodeFonts(env, fs));
        if (Is("ghostty", "GHOSTTY_RESOURCES_DIR"))
            return Bundled("Ghostty");
        if (Is("WezTerm", "WEZTERM_PANE"))
            return Bundled("WezTerm");
        if (env.GetEnvironmentVariable("TERM") == "xterm-kitty" || Is("kitty", "KITTY_WINDOW_ID"))
            return Bundled("kitty");
        if (Is("iTerm.app", "ITERM_SESSION_ID"))
            return FromFonts("iTerm2", Iterm2Fonts(env, fs, plistToXml ?? PlistToXml));
        if (env.GetEnvironmentVariable("WT_SESSION") is not null)
            return FromFonts("Windows Terminal", WindowsTerminalFonts(env, fs));
        if (env.GetEnvironmentVariable("TERM") == "alacritty" || Is("alacritty", "ALACRITTY_WINDOW_ID"))
            return FromFonts("Alacritty", AlacrittyFonts(env, fs));

        return new FontDetection(false, "Couldn't tell which font the terminal uses");
    }

    // Family names say "Nerd Font"; short and PostScript names end in NF, NFM or NFP.
    public static bool IsNerdFont(string font) => NerdFontName().IsMatch(font);

    [GeneratedRegex(@"(?i:nerd ?font)|NF[MP]?\b")]
    private static partial Regex NerdFontName();

    private static FontDetection Bundled(string terminal) => new(true, $"{terminal} bundles the Nerd Font symbols");

    private static FontDetection FromFonts(string terminal, IReadOnlyList<string>? fonts)
    {
        if (fonts is null or [])
            return new FontDetection(false, $"Couldn't read {terminal}'s font");
        if (fonts.FirstOrDefault(IsNerdFont) is { } nerd)
            return new FontDetection(true, $"{terminal} uses {nerd}");
        return new FontDetection(false, $"{terminal} uses {fonts[0]}, which isn't a Nerd Font");
    }

    private static List<string>? Iterm2Fonts(IEnvironment env, IFileSystem fs, Func<string, string?> plistToXml)
    {
        var path = fs.Path.Combine(env.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences", "com.googlecode.iterm2.plist");
        if (!fs.File.Exists(path) || plistToXml(path) is not { } xml) return null;

        Dictionary<string, XElement> preferences;
        try
        {
            preferences = PlistDict(XDocument.Parse(xml).Root?.Element("dict"));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var profiles = preferences.GetValueOrDefault("New Bookmarks")?.Elements("dict").Select(PlistDict).ToList() ?? [];
        var name = env.GetEnvironmentVariable("ITERM_PROFILE");
        var defaultGuid = preferences.GetValueOrDefault("Default Bookmark Guid")?.Value;
        var profile = profiles.FirstOrDefault(p => name is not null && p.GetValueOrDefault("Name")?.Value == name)
            ?? profiles.FirstOrDefault(p => p.GetValueOrDefault("Guid")?.Value == defaultGuid);
        if (profile is null) return null;

        // Stored as "<PostScript name> <size>".
        static string Font(string value) => value.LastIndexOf(' ') is > 0 and var space ? value[..space] : value;

        var fonts = new List<string>();
        if (profile.GetValueOrDefault("Normal Font")?.Value is { } normal) fonts.Add(Font(normal));
        // Glyphs outside ASCII, the icons included, come from this font when it's enabled.
        if (profile.GetValueOrDefault("Use Non-ASCII Font")?.Name == "true" && profile.GetValueOrDefault("Non Ascii Font")?.Value is { } nonAscii)
            fonts.Add(Font(nonAscii));
        return fonts;
    }

    private static Dictionary<string, XElement> PlistDict(XElement? dict)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var children = dict?.Elements().ToList() ?? [];
        for (var i = 0; i + 1 < children.Count; i += 2)
        {
            if (children[i].Name == "key")
                result[children[i].Value] = children[i + 1];
        }
        return result;
    }

    // iTerm2 saves its preferences as a binary plist.
    private static string? PlistToXml(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("plutil", ["-convert", "xml1", "-o", "-", path])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(2000) || process.ExitCode != 0) return null;
            return output.Result;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static List<string>? WindowsTerminalFonts(IEnvironment env, IFileSystem fs)
    {
        var local = env.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            fs.Path.Combine(local, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
            fs.Path.Combine(local, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
            fs.Path.Combine(local, "Microsoft", "Windows Terminal", "settings.json"),
        ];
        if (ReadJson(fs, candidates.FirstOrDefault(fs.File.Exists))?["profiles"] is not { } profiles) return null;

        static string? Face(JsonNode? profile) => String(profile?["font"]?["face"]) ?? String(profile?["fontFace"]);

        var list = profiles as JsonArray ?? profiles["list"] as JsonArray;
        var id = env.GetEnvironmentVariable("WT_PROFILE_ID");
        var profile = list?.FirstOrDefault(p => id is not null && string.Equals(String(p?["guid"]), id, StringComparison.OrdinalIgnoreCase));
        var face = Face(profile) ?? Face((profiles as JsonObject)?["defaults"]) ?? "Cascadia Mono";
        return [face];
    }

    private static List<string>? VsCodeFonts(IEnvironment env, IFileSystem fs)
    {
        var home = env.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var user = env.IsWindows ? fs.Path.Combine(env.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User")
            : env.IsMacOS ? fs.Path.Combine(home, "Library", "Application Support", "Code", "User")
            : fs.Path.Combine(home, ".config", "Code", "User");
        var path = fs.Path.Combine(user, "settings.json");
        var settings = fs.File.Exists(path) ? ReadJson(fs, path) : null;
        // A CSS font list, and the terminal falls back through it glyph by glyph, so any Nerd Font in it counts.
        var family = String(settings?["terminal.integrated.fontFamily"]) ?? String(settings?["editor.fontFamily"]);
        return family is null ? ["the default font"] : [family];
    }

    private static List<string>? AlacrittyFonts(IEnvironment env, IFileSystem fs)
    {
        var home = env.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = env.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : fs.Path.Combine(home, ".config");
        string[] candidates =
        [
            fs.Path.Combine(xdg, "alacritty", "alacritty.toml"),
            fs.Path.Combine(env.GetFolderPath(Environment.SpecialFolder.ApplicationData), "alacritty", "alacritty.toml"),
            fs.Path.Combine(home, ".alacritty.toml"),
        ];
        if (candidates.FirstOrDefault(fs.File.Exists) is not { } path) return ["the default font"];

        string toml;
        try
        {
            toml = fs.File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        var families = TomlFamily().Matches(toml).Select(m => m.Groups[1].Value).ToList();
        return families.Count > 0 ? families : ["the default font"];
    }

    [GeneratedRegex("""^\s*family\s*=\s*["']([^"']+)["']""", RegexOptions.Multiline)]
    private static partial Regex TomlFamily();

    private static JsonNode? ReadJson(IFileSystem fs, string? path)
    {
        if (path is null) return null;
        try
        {
            return JsonNode.Parse(fs.File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? String(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
}
