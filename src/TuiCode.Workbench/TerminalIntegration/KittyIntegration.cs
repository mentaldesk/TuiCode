using System.IO.Abstractions;
using System.Text;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.TerminalIntegration;

/// <summary>
/// kitty integration via <c>~/.config/kitty/tuicode.conf</c>, whose mappings only apply while the
/// focused window has the <c>TUICODE_ACTIVE=1</c> user var that <see cref="WorkbenchHost"/> sets.
/// Like WezTerm's, the user includes it from <c>kitty.conf</c> themselves.
/// </summary>
public sealed class KittyIntegration : ITerminalIntegration
{
    internal const int CurrentVersion = 2;
    internal const string ConfigFileName = "tuicode.conf";
    internal const string VersionMarker = "# TuiCodeIntegrationVersion:";
    internal const string FocusCondition = "--when-focus-on var:TUICODE_ACTIVE=1";

    /// <summary>The line the user adds to their <c>kitty.conf</c> to load the mappings.</summary>
    public const string ActivationSnippet = "include tuicode.conf";

    private readonly IFileSystem _fileSystem;
    private readonly IEnvironment _environment;

    public KittyIntegration(IFileSystem fileSystem, IEnvironment environment)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public string Id => "kitty";

    public string DisplayName => "kitty";

    public bool IsAvailable() =>
        _environment.IsMacOS &&
        (_environment.GetEnvironmentVariable("TERM") == "xterm-kitty" ||
         !string.IsNullOrEmpty(_environment.GetEnvironmentVariable("KITTY_WINDOW_ID")));

    public TerminalIntegrationStatus GetStatus()
    {
        var configPath = GetConfigPath();
        if (!_fileSystem.File.Exists(configPath))
            return TerminalIntegrationStatus.NotInstalled;

        return TryReadVersion(_fileSystem.File.ReadAllText(configPath)) == CurrentVersion
            ? TerminalIntegrationStatus.Installed
            : TerminalIntegrationStatus.Stale;
    }

    public void Install()
    {
        _fileSystem.Directory.CreateDirectory(GetConfigDirectory());
        _fileSystem.File.WriteAllText(GetConfigPath(), Config, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void Uninstall()
    {
        var configPath = GetConfigPath();
        if (_fileSystem.File.Exists(configPath))
            _fileSystem.File.Delete(configPath);
    }

    public string? PostInstallInstructions =>
        $"""
        TuiCode deliberately doesn't edit kitty.conf for you.

        To complete the integration, add this line to ~/.config/kitty/kitty.conf:

            {ActivationSnippet}

        Then reload kitty's config (default: Ctrl+Cmd+,).
        """;

    internal string GetConfigDirectory() =>
        _fileSystem.Path.Combine(
            _environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "kitty");

    internal string GetConfigPath() => _fileSystem.Path.Combine(GetConfigDirectory(), ConfigFileName);

    private static int? TryReadVersion(string text)
    {
        var idx = text.IndexOf(VersionMarker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var lineEnd = text.IndexOf('\n', idx);
        if (lineEnd < 0) lineEnd = text.Length;
        var rest = text[(idx + VersionMarker.Length)..lineEnd].Trim();
        return int.TryParse(rest, out var v) ? v : null;
    }

    /// <summary>Each kitty key and the bytes it sends: the same set as WezTerm's <c>tuicode</c> key table.</summary>
    internal static readonly (string Key, string Text)[] Mappings =
    [
        ("cmd+c", @"\x03"),
        ("cmd+v", @"\x16"),
        ("cmd+x", @"\x18"),
        ("cmd+z", @"\x1a"),
        ("cmd+shift+z", @"\x19"),
        ("cmd+a", @"\x01"),
        ("cmd+backspace", @"\x1b[127;6u"),
        ("opt+backspace", @"\x1b[127;5u"),
        ("cmd+left", @"\x1b[H"),
        ("cmd+right", @"\x1b[F"),
        ("cmd+shift+left", @"\x1b[1;2H"),
        ("cmd+shift+right", @"\x1b[1;2F"),
        ("opt+left", @"\x1b[1;5D"),
        ("opt+right", @"\x1b[1;5C"),
        ("opt+shift+left", @"\x1b[1;6D"),
        ("opt+shift+right", @"\x1b[1;6C"),
        ("cmd+up", @"\x1b[1;5H"),
        ("cmd+down", @"\x1b[1;5F"),
        ("cmd+shift+up", @"\x1b[1;6H"),
        ("cmd+shift+down", @"\x1b[1;6F"),
        ("page_up", @"\x1b[5~"),
        ("page_down", @"\x1b[6~"),
        ("shift+page_up", @"\x1b[5;2~"),
        ("shift+page_down", @"\x1b[6;2~"),
        ("ctrl+page_up", @"\x1b[5;5~"),
        ("ctrl+page_down", @"\x1b[6;5~"),
        ("opt+delete", @"\x04"),
        ("cmd+delete", @"\x1b[3;5~"),
    ];

    // `all`, not `normal,application`: TuiCode turns on the kitty keyboard protocol, which is a mode of its own.
    internal static readonly string Config =
        $"{VersionMarker} {CurrentVersion}\n" +
        $"# Written by TuiCode. Load it from kitty.conf with: {ActivationSnippet}\n" +
        string.Concat(Mappings.Select(m => $"map {FocusCondition} {m.Key} send_text all {m.Text}\n"));
}
