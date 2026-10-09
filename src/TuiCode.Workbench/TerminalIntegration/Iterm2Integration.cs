using System.Globalization;
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
/// <para>The profile maps macOS shortcuts (Cmd+C/X/Z/A, Cmd+arrows, Shift+Cmd+arrows) onto the
/// CSI sequences Terminal.Gui's <c>TextView</c> understands.</para>
/// <para>Bound Hosts matches both <c>TuiCode*</c> (upstream binary name) and <c>tuicode*</c>
/// (Homebrew rename) — iTerm2's matcher is case-sensitive, so both patterns are required to cover
/// every install method.</para>
/// <para>The profile carries the theme's cursor colour: iTerm2 reapplies a profile's colours whenever it
/// switches a session to it, over the OSC 12 <see cref="WorkbenchHost"/> sends (#299).</para>
/// <para>Uses a stable GUID so reinstalls overwrite the same profile in place. Status is determined
/// by reading the on-disk file's <c>TuiCodeIntegrationVersion</c> marker and comparing to
/// <see cref="CurrentProfileVersion"/>.</para>
/// </remarks>
public sealed class Iterm2Integration : ITerminalIntegration, ITerminalCursorColour
{
    internal const string ProfileGuid = "a21365eb-a2a0-4260-b0b7-e7368856dc65";
    internal const int CurrentProfileVersion = 4;
    internal const string ProfileFileName = "tuicode.json";

    private readonly IFileSystem _fileSystem;
    private readonly IEnvironment _environment;

    public Iterm2Integration(IFileSystem fileSystem, IEnvironment environment)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public string? CursorColour { get; set; }

    public string Id => "iterm2";

    public string DisplayName => "iTerm2";

    public string ClipboardInstructions =>
        "To copy over SSH, tick Settings → General → Selection →\n" +
        "\"Applications in terminal may access clipboard\".";

    public bool IsAvailable() =>
        string.Equals(
            _environment.GetEnvironmentVariable("TERM_PROGRAM"),
            "iTerm.app",
            StringComparison.Ordinal);

    public TerminalIntegrationStatus GetStatus()
    {
        if (!_fileSystem.File.Exists(GetProfilePath()))
            return TerminalIntegrationStatus.NotInstalled;

        return ReadInstalled() is (CurrentProfileVersion, not null, not null)
            ? TerminalIntegrationStatus.Installed
            : TerminalIntegrationStatus.Stale;
    }

    public void Install()
    {
        var dir = GetProfileDirectory();
        _fileSystem.Directory.CreateDirectory(dir);
        _fileSystem.File.WriteAllText(GetProfilePath(), ProfileJson(ItermColour.Parse(CursorColour)), Encoding.UTF8);
    }

    public void Refresh()
    {
        if (ItermColour.Parse(CursorColour) is not { } cursor || !_fileSystem.File.Exists(GetProfilePath()))
            return;
        if (ReadInstalled() is not (CurrentProfileVersion, { } light, { } dark) || light != cursor || dark != cursor)
            Install();
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

    private (int? Version, ItermColour? Light, ItermColour? Dark)? ReadInstalled()
    {
        try
        {
            using var stream = _fileSystem.File.OpenRead(GetProfilePath());
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("Profiles", out var profiles) ||
                profiles.ValueKind != JsonValueKind.Array ||
                profiles.GetArrayLength() == 0)
            {
                return (null, null, null);
            }

            var first = profiles[0];
            return (TryReadVersion(first), TryReadColour(first, "Cursor Color (Light)"), TryReadColour(first, "Cursor Color (Dark)"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? TryReadVersion(JsonElement profile) =>
        profile.TryGetProperty("TuiCodeIntegrationVersion", out var v) &&
        v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt32(out var i) ? i : null;

    private static ItermColour? TryReadColour(JsonElement profile, string key)
    {
        if (!profile.TryGetProperty(key, out var colour) || colour.ValueKind != JsonValueKind.Object)
            return null;
        return Component(colour, "Red Component") is { } r &&
               Component(colour, "Green Component") is { } g &&
               Component(colour, "Blue Component") is { } b
            ? new ItermColour(r, g, b)
            : null;
    }

    private static double? Component(JsonElement colour, string key) =>
        colour.TryGetProperty(key, out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null;

    private static string CursorColourKeys(ItermColour? cursor)
    {
        if (cursor is not { } c) return "";
        var components = string.Create(CultureInfo.InvariantCulture,
            $$"""{ "Red Component" : {{c.Red:R}}, "Green Component" : {{c.Green:R}}, "Blue Component" : {{c.Blue:R}}, "Color Space" : "sRGB" }""");
        return $"""
            "Cursor Color (Light)" : {components},
                  "Cursor Color (Dark)" : {components},
            """;
    }

    // Raw JSON: keys / values mirror what the Homebrew formula used to ship, with two changes —
    // Bound Hosts adds the lowercase "tuicode*" pattern (Homebrew renames the binary), and a
    // TuiCodeIntegrationVersion marker so we can detect stale installs on upgrade.
    // Shifted letters are keyed by the shifted character: Cmd+Shift+Z is 0x5a, never 0x7a (#46).
    internal static string ProfileJson(ItermColour? cursor) => $$"""
        {
          "Profiles" : [
            {
              "TuiCodeIntegrationVersion" : {{CurrentProfileVersion}},
              "Bound Hosts" : ["&TuiCode*", "&tuicode*"],
              "Use Separate Colors for Light and Dark Mode" : true,
              {{CursorColourKeys(cursor)}}
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
                "0x5a-0x120000"   : { "Action" : 11, "Text" : "0x19" },
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
                "0x7f-0x80000"    : { "Action" : 10, "Text" : "[127;5u" },
                "0x63-0x100000"   : { "Action" : 11, "Text" : "0x03" }
              }
            }
          ]
        }
        """;
}
