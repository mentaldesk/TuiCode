using System.IO.Abstractions;
using System.Text;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.TerminalIntegration;

/// <summary>
/// WezTerm integration via a Lua module dropped at <c>~/.config/wezterm/tuicode.lua</c>. We
/// deliberately do not touch the user's <c>wezterm.lua</c> — WezTerm users tend to treat that
/// file as personal config, so the user wires the module in themselves with a one-liner. The
/// snippet is surfaced in the Settings panel and printed by the CLI installer.
/// </summary>
/// <remarks>
/// <para>The Lua module defines a <c>tuicode</c> key table that sends the same byte sequences as
/// the iTerm2 dynamic profile (Cmd+letter → control bytes, Cmd/Opt+arrows → CSI sequences) and
/// listens for the <c>TUICODE_ACTIVE</c> user-var to push/pop the table per pane. TuiCode emits
/// the user-var via OSC 1337 from <see cref="WorkbenchHost"/> on startup and shutdown.</para>
/// <para>Status reflects the module file only — we can't reliably detect whether the user has
/// required it from <c>wezterm.lua</c> (they might do it conditionally or via another module).
/// A <c>TuiCodeIntegrationVersion</c> marker comment drives staleness detection.</para>
/// </remarks>
public sealed class WezTermIntegration : ITerminalIntegration
{
    internal const int CurrentVersion = 2;
    internal const string ModuleFileName = "tuicode.lua";
    internal const string VersionMarker = "-- TuiCodeIntegrationVersion:";

    /// <summary>The one-liner the user pastes into their <c>wezterm.lua</c> to activate the module.</summary>
    public const string ActivationSnippet = "require 'tuicode'.apply(config)";

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
        if (!_fileSystem.File.Exists(modulePath))
            return TerminalIntegrationStatus.NotInstalled;

        var moduleText = _fileSystem.File.ReadAllText(modulePath);
        return TryReadVersion(moduleText) == CurrentVersion
            ? TerminalIntegrationStatus.Installed
            : TerminalIntegrationStatus.Stale;
    }

    public void Install()
    {
        _fileSystem.Directory.CreateDirectory(GetConfigDirectory());
        _fileSystem.File.WriteAllText(GetModulePath(), ModuleLua, Encoding.UTF8);
    }

    public void Uninstall()
    {
        var modulePath = GetModulePath();
        if (_fileSystem.File.Exists(modulePath))
            _fileSystem.File.Delete(modulePath);
    }

    public string? PostInstallInstructions =>
        $"""
        Add this line to your wezterm.lua (anywhere after `local config = …`):

            {ActivationSnippet}

        TuiCode deliberately doesn't edit wezterm.lua for you.
        Reload WezTerm's config (default: Cmd+Shift+R) once added.
        """;

    internal string GetConfigDirectory() =>
        _fileSystem.Path.Combine(
            _environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "wezterm");

    internal string GetModulePath() => _fileSystem.Path.Combine(GetConfigDirectory(), ModuleFileName);

    private static int? TryReadVersion(string text)
    {
        var idx = text.IndexOf(VersionMarker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var lineEnd = text.IndexOf('\n', idx);
        if (lineEnd < 0) lineEnd = text.Length;
        var rest = text[(idx + VersionMarker.Length)..lineEnd].Trim();
        return int.TryParse(rest, out var v) ? v : null;
    }

    // Mirrors the iTerm2 Keyboard Map: Cmd+letter → control bytes, Cmd/Opt+arrows + Shift variants
    // → CSI sequences TextView understands. The user-var-changed handler activates the table when
    // TuiCode signals startup (TUICODE_ACTIVE=1) and pops it on shutdown (=0) so the user's normal
    // WezTerm bindings (Cmd+C for selection copy, etc.) remain untouched in their shell.
    internal const string ModuleLua = """
        -- TuiCodeIntegrationVersion: 2
        local M = {}

        function M.apply(config)
          local wezterm = require 'wezterm'

          -- WezTerm gates the kitty keyboard protocol behind this flag — without it, TG's
          -- CSI > 1 u push request is ignored and Ctrl+non-letter chords (Ctrl+, Ctrl+E, …)
          -- arrive as bare bytes. Other terminals (iTerm2, Ghostty, kitty) respond to the
          -- push request unconditionally. Safe to set globally: it only opts WezTerm in to
          -- *responding* to per-app requests; apps that don't ask see no behavior change.
          config.enable_kitty_keyboard = true

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
