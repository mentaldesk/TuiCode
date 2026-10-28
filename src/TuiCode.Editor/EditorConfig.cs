using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TuiCode.Abstractions;

namespace TuiCode.Editor;

/// <summary>One <c>.editorconfig</c> file (#622): whether it's the root, and its sections in order.</summary>
public sealed record EditorConfigFile(bool Root, IReadOnlyList<EditorConfigSection> Sections)
{
    public static EditorConfigFile Parse(string text)
    {
        var root = false;
        var sections = new List<EditorConfigSection>();
        List<KeyValuePair<string, string>>? properties = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                properties = [];
                sections.Add(new EditorConfigSection(line[1..^1], properties));
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals < 0) continue;
            var key = line[..equals].Trim().ToLowerInvariant();
            var value = line[(equals + 1)..].Trim();
            if (properties is not null)
                properties.Add(new(key, value));
            else if (key == "root")
                root = value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return new EditorConfigFile(root, sections);
    }
}

public sealed record EditorConfigSection(string Glob, IReadOnlyList<KeyValuePair<string, string>> Properties);

/// <summary>Finds the <c>.editorconfig</c> rules for a file, from its folder up to the one marked <c>root = true</c>.</summary>
public static class EditorConfig
{
    public const string FileName = ".editorconfig";

    /// <summary>Each property that applies to <paramref name="fullPath"/>, keys and values lower-cased; <c>unset</c> ones are left out.</summary>
    public static IReadOnlyDictionary<string, string> PropertiesFor(IFileSystem fs, string fullPath)
    {
        var files = new List<(string Directory, EditorConfigFile File)>();
        for (var directory = fs.Path.GetDirectoryName(fullPath); !string.IsNullOrEmpty(directory); directory = fs.Path.GetDirectoryName(directory))
        {
            if (Read(fs, fs.Path.Combine(directory, FileName)) is not { } file) continue;
            files.Add((directory, file));
            if (file.Root) break;
        }

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = files.Count - 1; i >= 0; i--)
        {
            var (directory, file) = files[i];
            var relative = fs.Path.GetRelativePath(directory, fullPath).Replace('\\', '/');
            foreach (var section in file.Sections)
            {
                if (!EditorConfigGlob.Matches(section.Glob, relative)) continue;
                foreach (var (key, value) in section.Properties)
                    properties[key] = value.ToLowerInvariant();
            }
        }
        foreach (var unset in properties.Where(p => p.Value == "unset").Select(p => p.Key).ToList())
            properties.Remove(unset);
        return properties;
    }

    private static EditorConfigFile? Read(IFileSystem fs, string path)
    {
        try
        {
            return fs.File.Exists(path) ? EditorConfigFile.Parse(fs.File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>The four <c>.editorconfig</c> properties TuiCode follows; null where the file doesn't say.</summary>
public sealed record EditorConfigRules
{
    public static EditorConfigRules None { get; } = new();

    public bool? InsertSpaces { get; init; }
    public int? IndentSize { get; init; }
    public int? TabWidth { get; init; }
    public LineEnding? LineEnding { get; init; }
    public bool? InsertFinalNewline { get; init; }

    public bool SetsIndentation => InsertSpaces is not null || IndentSize is not null || TabWidth is not null;

    public static EditorConfigRules For(IFileSystem fs, string fullPath) => From(EditorConfig.PropertiesFor(fs, fullPath));

    public static EditorConfigRules From(IReadOnlyDictionary<string, string> properties)
    {
        string? Get(string key) => properties.TryGetValue(key, out var value) ? value : null;

        var tabWidth = Size(Get("tab_width"));
        var insertSpaces = Get("indent_style") switch { "space" => true, "tab" => (bool?)false, _ => null };
        var indentSize = Get("indent_size") == "tab" ? tabWidth : Size(Get("indent_size"));
        if (indentSize is null && insertSpaces == false) indentSize = tabWidth;
        return new EditorConfigRules
        {
            InsertSpaces = insertSpaces,
            IndentSize = indentSize,
            TabWidth = tabWidth ?? indentSize,
            LineEnding = Get("end_of_line") switch
            {
                "lf" => Abstractions.LineEnding.LF,
                "crlf" => Abstractions.LineEnding.CRLF,
                _ => null,
            },
            InsertFinalNewline = Get("insert_final_newline") switch { "true" => true, "false" => (bool?)false, _ => null },
        };
    }

    private static int? Size(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
            ? Math.Clamp(size, EditorSettings.MinIndentSize, EditorSettings.MaxIndentSize)
            : null;
}

/// <summary>An <c>.editorconfig</c> section's glob, matched against a path relative to that file's folder.</summary>
public static class EditorConfigGlob
{
    private static readonly ConcurrentDictionary<string, (Regex Regex, List<(int Min, int Max)> Ranges)> Compiled = new();

    public static bool Matches(string glob, string relativePath)
    {
        var (regex, ranges) = Compiled.GetOrAdd(glob, Compile);
        var match = regex.Match(relativePath);
        if (!match.Success) return false;
        for (var i = 0; i < ranges.Count; i++)
        {
            if (!int.TryParse(match.Groups[i + 1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                || n < ranges[i].Min || n > ranges[i].Max)
                return false;
        }
        return true;
    }

    private static (Regex, List<(int, int)>) Compile(string glob)
    {
        var ranges = new List<(int, int)>();
        string pattern;
        if (!HasSeparator(glob))
            pattern = "(?:.*/)?" + Translate(glob, ranges);
        else
            pattern = Translate(glob.StartsWith('/') ? glob[1..] : glob, ranges);
        return (new Regex("^" + pattern + "$", RegexOptions.CultureInvariant), ranges);
    }

    // A `/` inside `[...]` doesn't count: the glob still matches at any depth.
    private static bool HasSeparator(string glob)
    {
        for (var i = 0; i < glob.Length; i++)
        {
            if (glob[i] == '\\') i++;
            else if (glob[i] == '[' && ClosingBracket(glob, i) is { } close) i = close;
            else if (glob[i] == '/') return true;
        }
        return false;
    }

    private static string Translate(string glob, List<(int, int)> ranges)
    {
        var regex = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '\\' when i + 1 < glob.Length:
                    regex.Append(Regex.Escape(glob[++i].ToString()));
                    break;
                case '*' when i + 1 < glob.Length && glob[i + 1] == '*':
                    i++;
                    if (i + 1 < glob.Length && glob[i + 1] == '/' && (i == 1 || glob[i - 2] == '/'))
                    {
                        regex.Append("(?:.*/)?");
                        i++;
                    }
                    else
                    {
                        regex.Append(".*");
                    }
                    break;
                case '*':
                    regex.Append("[^/]*");
                    break;
                case '?':
                    regex.Append("[^/]");
                    break;
                case '[' when ClosingBracket(glob, i) is { } close && !glob.AsSpan(i, close - i).Contains('/'):
                    regex.Append(CharacterClass(glob[(i + 1)..close]));
                    i = close;
                    break;
                case '{' when ClosingBrace(glob, i) is { } close:
                    regex.Append(Braces(glob[(i + 1)..close], ranges));
                    i = close;
                    break;
                default:
                    regex.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        return regex.ToString();
    }

    private static string CharacterClass(string content)
    {
        var negated = content.Length > 0 && content[0] is '!' or '^';
        if (negated) content = content[1..];
        var body = new StringBuilder();
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '\\' && i + 1 < content.Length) c = content[++i];
            else if (c == '-' && i > 0 && i < content.Length - 1)
            {
                body.Append('-');
                continue;
            }
            body.Append(c is '\\' or ']' or '[' or '^' or '-' ? "\\" + c : c);
        }
        return (negated ? "[^/" : "[") + body + "]";
    }

    private static string Braces(string content, List<(int, int)> ranges)
    {
        if (Regex.Match(content, @"^([+-]?\d+)\.\.([+-]?\d+)$") is { Success: true } range)
        {
            var a = int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
            var b = int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
            ranges.Add((Math.Min(a, b), Math.Max(a, b)));
            return @"([+-]?\d+)";
        }
        var alternatives = SplitAlternatives(content);
        if (alternatives.Count == 1) return @"\{" + Translate(content, ranges) + @"\}";
        return "(?:" + string.Join("|", alternatives.Select(a => Translate(a, ranges))) + ")";
    }

    private static List<string> SplitAlternatives(string content)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            switch (content[i])
            {
                case '\\':
                    i++;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(content[start..i]);
                    start = i + 1;
                    break;
            }
        }
        parts.Add(content[start..]);
        return parts;
    }

    private static int? ClosingBracket(string glob, int open)
    {
        for (var i = open + 1; i < glob.Length; i++)
        {
            if (glob[i] == '\\') i++;
            else if (glob[i] == ']' && i > open + 1) return i;
        }
        return null;
    }

    private static int? ClosingBrace(string glob, int open)
    {
        var depth = 0;
        for (var i = open; i < glob.Length; i++)
        {
            if (glob[i] == '\\') i++;
            else if (glob[i] == '{') depth++;
            else if (glob[i] == '}' && --depth == 0) return i;
        }
        return null;
    }
}
