using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Settings;

/// <summary>A row of Settings › Language Servers. <see cref="Installed"/> is null when the language runs no server.</summary>
internal sealed record LanguageServerRow(SyntaxLanguage Language, LanguageServerSetting Setting, string Server, bool? Installed);

/// <summary>Settings › Language Servers' rows (#456): every highlighted language and the server it runs. TG-free for testing.</summary>
internal static class LanguageServerRows
{
    public const string NoServer = "—";

    public static IReadOnlyList<LanguageServerRow> Build(
        IEnumerable<SyntaxLanguage> languages,
        IReadOnlyDictionary<string, LanguageServerSetting> chosen,
        Func<string, bool> isInstalled) =>
        languages
            .OrderBy(language => language.Name, StringComparer.OrdinalIgnoreCase)
            .Select(language =>
            {
                var setting = KnownLanguageServers.Chosen(language.Id, chosen);
                return new LanguageServerRow(
                    language, setting, ServerName(language.Id, setting), setting.IsNone ? null : isInstalled(setting.Command));
            })
            .ToList();

    public static string ServerName(string languageId, LanguageServerSetting setting)
    {
        if (setting.IsNone) return NoServer;
        var name = KnownLanguageServers.Matching(languageId, setting)?.Name ?? ArgumentLine.Join([setting.Command, .. setting.Arguments]);
        return setting == KnownLanguageServers.Default(languageId) ? $"{name} (default)" : name;
    }

    public static string Header(IReadOnlyList<LanguageServerRow> rows) => Display(rows, "Language", "Server", "Installed");

    public static string Display(IReadOnlyList<LanguageServerRow> rows, LanguageServerRow row) =>
        Display(rows, row.Language.Name, row.Server, row.Installed switch { true => "✓", false => "✗", null => "" });

    private static string Display(IReadOnlyList<LanguageServerRow> rows, string language, string server, string installed)
    {
        var languageWidth = Math.Max("Language".Length, rows.Count == 0 ? 0 : rows.Max(r => r.Language.Name.Length)) + 2;
        var serverWidth = Math.Max("Server".Length, rows.Count == 0 ? 0 : rows.Max(r => r.Server.Length)) + 2;
        return $"{language.PadRight(languageWidth)}{server.PadRight(serverWidth)}{installed}".TrimEnd();
    }
}

/// <summary>A command's arguments as one line of text: split on spaces, with double quotes around an argument that holds one.</summary>
internal static class ArgumentLine
{
    public static IReadOnlyList<string> Split(string line)
    {
        var arguments = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started) arguments.Add(current.ToString());
                current.Clear();
                started = false;
            }
            else
            {
                current.Append(c);
                started = true;
            }
        }
        if (started) arguments.Add(current.ToString());
        return arguments;
    }

    public static string Join(IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Select(a => a.Length == 0 || a.Any(char.IsWhiteSpace) ? $"\"{a}\"" : a));
}
