using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.TerminalIntegration;

/// <summary>
/// iTerm2 integration via a Dynamic Profile JSON dropped into
/// <c>~/Library/Application Support/iTerm2/DynamicProfiles/tuicode.json</c>. iTerm2 watches that
/// directory and applies the profile to processes matching <c>Bound Hosts</c> automatically.
/// </summary>
/// <remarks>
/// <para>The profile maps macOS shortcuts (Cmd+C/V/X/Z/A, Cmd+arrows, Shift+Cmd+arrows) onto the
/// CSI sequences Terminal.Gui's <c>TextView</c> understands.</para>
/// <para>Bound Hosts matches both <c>TuiCode*</c> (upstream binary name) and <c>tuicode*</c>
/// (Homebrew rename) — iTerm2's matcher is case-sensitive, so both patterns are required to cover
/// every install method.</para>
/// <para>Uses a stable GUID so reinstalls overwrite the same profile in place. Status is determined
/// by reading the on-disk file's <c>TuiCodeIntegrationVersion</c> marker and comparing to
/// <see cref="CurrentProfileVersion"/>.</para>
/// </remarks>
public sealed class Iterm2Integration : ITerminalIntegration
{
    internal const string ProfileGuid = "a21365eb-a2a0-4260-b0b7-e7368856dc65";
    internal const int CurrentProfileVersion = 1;
    internal const string ProfileFileName = "tuicode.json";

    private readonly IFileSystem _fileSystem;
    private readonly IEnvironment _environment;

    public Iterm2Integration(IFileSystem fileSystem, IEnvironment environment)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public string Id => "iterm2";

    public string DisplayName => "iTerm2";

    public bool IsAvailable() =>
        string.Equals(
            _environment.GetEnvironmentVariable("TERM_PROGRAM"),
            "iTerm.app",
            StringComparison.Ordinal);

    public TerminalIntegrationStatus GetStatus()
    {
        var path = GetProfilePath();
        if (!_fileSystem.File.Exists(path))
            return TerminalIntegrationStatus.NotInstalled;

        int? installedVersion;
        try
        {
            using var stream = _fileSystem.File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            installedVersion = TryReadVersion(doc);
        }
        catch (JsonException)
        {
            return TerminalIntegrationStatus.Stale;
        }

        return installedVersion == CurrentProfileVersion
            ? TerminalIntegrationStatus.Installed
            : TerminalIntegrationStatus.Stale;
    }

    public void Install()
    {
        var dir = GetProfileDirectory();
        _fileSystem.Directory.CreateDirectory(dir);
        _fileSystem.File.WriteAllText(GetProfilePath(), ProfileJson, Encoding.UTF8);
    }

    public void Uninstall()
    {
        var path = GetProfilePath();
        if (_fileSystem.File.Exists(path))
            _fileSystem.File.Delete(path);
    }

    internal string GetProfileDirectory() =>
        _fileSystem.Path.Combine(
            _environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "iTerm2", "DynamicProfiles");

    internal string GetProfilePath() =>
        _fileSystem.Path.Combine(GetProfileDirectory(), ProfileFileName);

    private static int? TryReadVersion(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("Profiles", out var profiles) ||
            profiles.ValueKind != JsonValueKind.Array ||
            profiles.GetArrayLength() == 0)
        {
            return null;
        }

        var first = profiles[0];
        if (!first.TryGetProperty("TuiCodeIntegrationVersion", out var v) ||
            v.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return v.TryGetInt32(out var i) ? i : null;
    }

    // Raw JSON: keys / values mirror what the Homebrew formula used to ship, with two changes —
    // Bound Hosts adds the lowercase "tuicode*" pattern (Homebrew renames the binary), and a
    // TuiCodeIntegrationVersion marker so we can detect stale installs on upgrade.
    internal const string ProfileJson = """
        {
          "Profiles" : [
            {
              "TuiCodeIntegrationVersion" : 1,
              "Bound Hosts" : ["&TuiCode*", "&tuicode*"],
              "Use Separate Colors for Light and Dark Mode" : true,
              "Rewritable" : true,
              "Name" : "TuiCode",
              "Guid" : "a21365eb-a2a0-4260-b0b7-e7368856dc65",
              "Keyboard Map" : {
                "0x7f-0x100000"   : { "Action" : 10, "Text" : "[127;6u" },
                "0xf72c-0x200000" : { "Action" : 10, "Text" : "[5~" },
                "0xf72d-0x220000" : { "Action" : 10, "Text" : "[6;2~" },
                "0xf702-0x320000" : { "Action" : 10, "Text" : "[1;2H" },
                "0xf72d-0x200000" : { "Action" : 10, "Text" : "[6~" },
                "0xf702-0x300000" : { "Action" : 10, "Text" : "[H" },
                "0xf703-0x320000" : { "Action" : 10, "Text" : "[1;2F" },
                "0xf703-0x300000" : { "Action" : 10, "Text" : "[F" },
                "0x7a-0x120000"   : { "Action" : 11, "Text" : "0x19" },
                "0xf702-0x2a0000" : { "Action" : 10, "Text" : "[1;6D" },
                "0xf703-0x280000" : { "Action" : 10, "Text" : "[1;5C" },
                "0xf703-0x2a0000" : { "Action" : 10, "Text" : "[1;6C" },
                "0xf700-0x320000" : { "Action" : 10, "Text" : "[1;6H" },
                "0xf702-0x280000" : { "Action" : 10, "Text" : "[1;5D" },
                "0xf700-0x300000" : { "Action" : 10, "Text" : "[1;5H" },
                "0xf701-0x320000" : { "Action" : 10, "Text" : "[1;6F" },
                "0x61-0x100000"   : { "Action" : 11, "Text" : "0x01" },
                "0xf701-0x300000" : { "Action" : 10, "Text" : "[1;5F" },
                "0xf72c-0x220000" : { "Action" : 10, "Text" : "[5;2~" },
                "0x78-0x100000"   : { "Action" : 11, "Text" : "0x18" },
                "0xf728-0x280000" : { "Action" : 10, "Text" : "[3;5~" },
                "0x7a-0x100000"   : { "Action" : 11, "Text" : "0x1a" },
                "0xf728-0x200000" : { "Action" : 11, "Text" : "0x04" },
                "0x76-0x100000"   : { "Action" : 11, "Text" : "0x16" },
                "0x7f-0x80000"    : { "Action" : 10, "Text" : "[127;5u" },
                "0x63-0x100000"   : { "Action" : 11, "Text" : "0x03" }
              }
            }
          ]
        }
        """;
}
