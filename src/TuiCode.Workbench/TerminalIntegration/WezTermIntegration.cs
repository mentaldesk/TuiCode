using System.IO.Abstractions;
using System.Text;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.TerminalIntegration;

/// <summary>
/// WezTerm integration via a Lua module dropped at <c>~/.config/wezterm/tuicode.lua</c> plus a
/// fenced <c>-- TuiCode begin / -- TuiCode end</c> block appended to the user's
/// <c>wezterm.lua</c> that requires it.
/// </summary>
/// <remarks>
/// <para>WezTerm has no auto-watched profiles directory like iTerm2's, so we edit the user's
/// config directly. The fenced block makes the edit reversible and visible; the
/// <c>--install-terminal-integration</c> CLI flag and Settings UI button are the explicit consent
/// points. Idempotent: re-running replaces the block in place.</para>
/// <para>The Lua module defines a <c>tuicode</c> key table that sends the same byte sequences as
/// the iTerm2 dynamic profile (Cmd+letter → control bytes, Cmd/Opt+arrows → CSI sequences) and
/// listens for the <c>TUICODE_ACTIVE</c> user-var to push/pop the table per pane. TuiCode emits
/// the user-var via OSC 1337 from <see cref="WorkbenchHost"/> on startup and shutdown.</para>
/// <para>Status is determined by a <c>TuiCodeIntegrationVersion</c> marker comment in both the
/// module and the fenced block; mismatch or missing marker is <see cref="TerminalIntegrationStatus.Stale"/>.</para>
/// </remarks>
public sealed class WezTermIntegration : ITerminalIntegration
{
    internal const int CurrentVersion = 1;
    internal const string ModuleFileName = "tuicode.lua";
    internal const string ConfigFileName = "wezterm.lua";
    internal const string BeginMarker = "-- TuiCode begin";
    internal const string EndMarker = "-- TuiCode end";
    internal const string VersionMarker = "-- TuiCodeIntegrationVersion:";

    private readonly IFileSystem _fileSystem;
    private readonly IEnvironment _environment;

    public WezTermIntegration(IFileSystem fileSystem, IEnvironment environment)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public string Id => "wezterm";

    public string DisplayName => "WezTerm";

    public bool IsAvailable() =>
        string.Equals(
            _environment.GetEnvironmentVariable("TERM_PROGRAM"),
            "WezTerm",
            StringComparison.Ordinal);

    public TerminalIntegrationStatus GetStatus()
    {
        var modulePath = GetModulePath();
        var configPath = GetConfigPath();

        if (!_fileSystem.File.Exists(modulePath) || !_fileSystem.File.Exists(configPath))
            return TerminalIntegrationStatus.NotInstalled;

        var configText = _fileSystem.File.ReadAllText(configPath);
        var block = ExtractBlock(configText);
        if (block is null)
            return TerminalIntegrationStatus.NotInstalled;

        var moduleText = _fileSystem.File.ReadAllText(modulePath);
        if (TryReadVersion(moduleText) == CurrentVersion && TryReadVersion(block) == CurrentVersion)
            return TerminalIntegrationStatus.Installed;

        return TerminalIntegrationStatus.Stale;
    }

    public void Install()
    {
        var dir = GetConfigDirectory();
        _fileSystem.Directory.CreateDirectory(dir);
        _fileSystem.File.WriteAllText(GetModulePath(), ModuleLua, Encoding.UTF8);

        var configPath = GetConfigPath();
        var existing = _fileSystem.File.Exists(configPath)
            ? _fileSystem.File.ReadAllText(configPath)
            : string.Empty;

        _fileSystem.File.WriteAllText(configPath, ReplaceOrAppendBlock(existing), Encoding.UTF8);
    }

    public void Uninstall()
    {
        var modulePath = GetModulePath();
        if (_fileSystem.File.Exists(modulePath))
            _fileSystem.File.Delete(modulePath);

        var configPath = GetConfigPath();
        if (!_fileSystem.File.Exists(configPath)) return;

        var stripped = StripBlock(_fileSystem.File.ReadAllText(configPath));
        if (string.IsNullOrWhiteSpace(stripped))
            _fileSystem.File.Delete(configPath);
        else
            _fileSystem.File.WriteAllText(configPath, stripped, Encoding.UTF8);
    }

    internal string GetConfigDirectory() =>
        _fileSystem.Path.Combine(
            _environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "wezterm");

    internal string GetModulePath() => _fileSystem.Path.Combine(GetConfigDirectory(), ModuleFileName);

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

    private static string? ExtractBlock(string text)
    {
        var start = text.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (start < 0) return null;
        var end = text.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0) return null;
        return text[start..(end + EndMarker.Length)];
    }

    private static string ReplaceOrAppendBlock(string existing)
    {
        var start = existing.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = existing.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (end >= 0)
                return existing[..start] + FencedBlock + existing[(end + EndMarker.Length)..];
        }

        if (existing.Length == 0) return FencedBlock + "\n";
        var sep = existing.EndsWith('\n') ? "\n" : "\n\n";
        return existing + sep + FencedBlock + "\n";
    }

    private static string StripBlock(string existing)
    {
        var start = existing.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (start < 0) return existing;
        var end = existing.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0) return existing;
        var after = end + EndMarker.Length;
        if (after < existing.Length && existing[after] == '\n') after++;
        var before = start;
        if (before > 0 && existing[before - 1] == '\n') before--;
        return existing[..before] + existing[after..];
    }

    // The block requires the embedded module by name. WezTerm auto-adds `~/.config/wezterm` to
    // the Lua path, so `require 'tuicode'` resolves to tuicode.lua next to wezterm.lua.
    internal const string FencedBlock = """
        -- TuiCode begin
        -- TuiCodeIntegrationVersion: 1
        local ok_tuicode, tuicode = pcall(require, 'tuicode')
        if ok_tuicode then tuicode.apply(config) end
        -- TuiCode end
        """;

    // Mirrors the iTerm2 Keyboard Map: Cmd+letter → control bytes, Cmd/Opt+arrows + Shift variants
    // → CSI sequences TextView understands. The user-var-changed handler activates the table when
    // TuiCode signals startup (TUICODE_ACTIVE=1) and pops it on shutdown (=0) so the user's normal
    // WezTerm bindings (Cmd+C for selection copy, etc.) remain untouched in their shell.
    internal const string ModuleLua = """
        -- TuiCodeIntegrationVersion: 1
        local M = {}

        function M.apply(config)
          local wezterm = require 'wezterm'

          config.key_tables = config.key_tables or {}
          config.key_tables.tuicode = {
            { key = 'c', mods = 'CMD',       action = wezterm.action.SendString '\x03' },
            { key = 'v', mods = 'CMD',       action = wezterm.action.SendString '\x16' },
            { key = 'x', mods = 'CMD',       action = wezterm.action.SendString '\x18' },
            { key = 'z', mods = 'CMD',       action = wezterm.action.SendString '\x1a' },
            { key = 'z', mods = 'CMD|SHIFT', action = wezterm.action.SendString '\x19' },
            { key = 'a', mods = 'CMD',       action = wezterm.action.SendString '\x01' },
            { key = 'Backspace', mods = 'CMD',       action = wezterm.action.SendString '\x1b[127;6u' },
            { key = 'Backspace', mods = 'OPT',       action = wezterm.action.SendString '\x1b[127;5u' },
            { key = 'LeftArrow',  mods = 'CMD',       action = wezterm.action.SendString '\x1b[H' },
            { key = 'RightArrow', mods = 'CMD',       action = wezterm.action.SendString '\x1b[F' },
            { key = 'LeftArrow',  mods = 'CMD|SHIFT', action = wezterm.action.SendString '\x1b[1;2H' },
            { key = 'RightArrow', mods = 'CMD|SHIFT', action = wezterm.action.SendString '\x1b[1;2F' },
            { key = 'LeftArrow',  mods = 'OPT',       action = wezterm.action.SendString '\x1b[1;5D' },
            { key = 'RightArrow', mods = 'OPT',       action = wezterm.action.SendString '\x1b[1;5C' },
            { key = 'LeftArrow',  mods = 'OPT|SHIFT', action = wezterm.action.SendString '\x1b[1;6D' },
            { key = 'RightArrow', mods = 'OPT|SHIFT', action = wezterm.action.SendString '\x1b[1;6C' },
            { key = 'UpArrow',    mods = 'CMD',       action = wezterm.action.SendString '\x1b[1;5H' },
            { key = 'DownArrow',  mods = 'CMD',       action = wezterm.action.SendString '\x1b[1;5F' },
            { key = 'UpArrow',    mods = 'CMD|SHIFT', action = wezterm.action.SendString '\x1b[1;6H' },
            { key = 'DownArrow',  mods = 'CMD|SHIFT', action = wezterm.action.SendString '\x1b[1;6F' },
            { key = 'PageUp',     mods = 'NONE',      action = wezterm.action.SendString '\x1b[5~' },
            { key = 'PageDown',   mods = 'NONE',      action = wezterm.action.SendString '\x1b[6~' },
            { key = 'PageUp',     mods = 'SHIFT',     action = wezterm.action.SendString '\x1b[5;2~' },
            { key = 'PageDown',   mods = 'SHIFT',     action = wezterm.action.SendString '\x1b[6;2~' },
            { key = 'Delete',     mods = 'OPT',       action = wezterm.action.SendString '\x04' },
            { key = 'Delete',     mods = 'CMD',       action = wezterm.action.SendString '\x1b[3;5~' },
          }

          wezterm.on('user-var-changed', function(window, pane, name, value)
            if name ~= 'TUICODE_ACTIVE' then return end
            if value == '1' then
              window:perform_action(
                wezterm.action.ActivateKeyTable { name = 'tuicode', one_shot = false, replace_current = true },
                pane)
            else
              window:perform_action(wezterm.action.PopKeyTable, pane)
            end
          end)
        end

        return M
        """;
}
