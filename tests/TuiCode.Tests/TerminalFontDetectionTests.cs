using TuiCode.Icons;
using TuiCode.Workbench.Icons;

namespace TuiCode.Tests;

public class TerminalFontDetectionTests
{
    private const string Home = "/home/me";
    private readonly MockFileSystem _fs = new();
    private readonly FakeEnvironment _env = new FakeEnvironment()
        .SetFolder(Environment.SpecialFolder.UserProfile, Home)
        .SetFolder(Environment.SpecialFolder.LocalApplicationData, "/local")
        .SetFolder(Environment.SpecialFolder.ApplicationData, "/roaming");

    [Theory]
    [InlineData("JetBrainsMono Nerd Font")]
    [InlineData("JetBrainsMonoNerdFontMono-Regular")]
    [InlineData("CaskaydiaCove NF")]
    [InlineData("JetBrainsMonoNFM-Regular")]
    [InlineData("MesloLGS-NF-Regular")]
    [InlineData("'Hack Nerd Font', monospace")]
    public void Nerd_font_names_are_recognised(string font) =>
        Assert.True(TerminalFontDetection.IsNerdFont(font));

    [Theory]
    [InlineData("Menlo-Regular")]
    [InlineData("Cascadia Mono")]
    [InlineData("SF Mono")]
    [InlineData("CONFIGURED")]
    public void Other_fonts_are_not(string font) =>
        Assert.False(TerminalFontDetection.IsNerdFont(font));

    [Theory]
    [InlineData("TERM_PROGRAM", "ghostty", "Ghostty")]
    [InlineData("TERM_PROGRAM", "WezTerm", "WezTerm")]
    [InlineData("TERM", "xterm-kitty", "kitty")]
    [InlineData("KITTY_WINDOW_ID", "1", "kitty")]
    public void Terminals_that_bundle_the_symbols_have_them(string variable, string value, string terminal)
    {
        _env.Set(variable, value);

        var detection = Detect();

        Assert.True(detection.NerdFont);
        Assert.Contains(terminal, detection.Reason);
    }

    [Fact]
    public void A_terminal_is_still_recognised_inside_tmux()
    {
        _env.Set("TERM_PROGRAM", "tmux").Set("WEZTERM_PANE", "0");

        Assert.True(Detect().NerdFont);
    }

    [Fact]
    public void An_unrecognised_terminal_is_assumed_not_to_have_them() =>
        Assert.False(Detect().NerdFont);

    [Theory]
    [InlineData("Work", true)]
    [InlineData(null, false)]
    public void ITerm2_uses_the_sessions_profile_or_else_the_default(string? profile, bool nerdFont)
    {
        _env.Set("TERM_PROGRAM", "iTerm.app").Set("ITERM_PROFILE", profile);
        _fs.AddFile($"{Home}/Library/Preferences/com.googlecode.iterm2.plist", new MockFileData("binary"));

        var detection = Detect(ITerm2Plist);

        Assert.Equal(nerdFont, detection.NerdFont);
        Assert.Equal(nerdFont ? "iTerm2 uses JetBrainsMonoNFM-Regular" : "iTerm2 uses Menlo-Regular, which isn't a Nerd Font", detection.Reason);
    }

    [Fact]
    public void ITerm2s_non_ascii_font_counts_when_it_is_enabled()
    {
        _env.Set("TERM_PROGRAM", "iTerm.app").Set("ITERM_PROFILE", "Split");
        _fs.AddFile($"{Home}/Library/Preferences/com.googlecode.iterm2.plist", new MockFileData("binary"));

        Assert.True(Detect(ITerm2Plist).NerdFont);
    }

    [Fact]
    public void An_unreadable_iterm2_plist_is_not_a_nerd_font()
    {
        _env.Set("TERM_PROGRAM", "iTerm.app");
        _fs.AddFile($"{Home}/Library/Preferences/com.googlecode.iterm2.plist", new MockFileData("binary"));

        var detection = Detect(_ => null);

        Assert.False(detection.NerdFont);
        Assert.Equal("Couldn't read iTerm2's font", detection.Reason);
    }

    [Theory]
    [InlineData("{61c54bbd-c2c6-5271-96e7-009a87ff44bf}", "CaskaydiaCove NF")]
    [InlineData("{00000000-0000-0000-0000-000000000000}", "Cascadia Code")]
    public void Windows_terminal_uses_the_sessions_profile_font_or_else_the_defaults(string profileId, string font)
    {
        _env.Set("WT_SESSION", "x").Set("WT_PROFILE_ID", profileId);
        _fs.AddFile("/local/Packages/Microsoft.WindowsTerminal_8wekyb3d8bbwe/LocalState/settings.json", new MockFileData("""
            {
                // Comments and trailing commas are allowed.
                "profiles": {
                    "defaults": { "font": { "face": "Cascadia Code" } },
                    "list": [
                        { "guid": "{61C54BBD-C2C6-5271-96E7-009A87FF44BF}", "font": { "face": "CaskaydiaCove NF" } },
                    ],
                },
            }
            """));

        Assert.Contains(font, Detect().Reason);
    }

    [Fact]
    public void Vs_code_falls_back_through_its_font_list()
    {
        _env.Set("TERM_PROGRAM", "vscode").Set("ITERM_SESSION_ID", "leaked from the shell that launched VS Code");
        _fs.AddFile($"{Home}/Library/Application Support/Code/User/settings.json", new MockFileData("""
            { "terminal.integrated.fontFamily": "Menlo, 'Symbols Nerd Font Mono'" }
            """));

        var detection = Detect();

        Assert.True(detection.NerdFont);
        Assert.StartsWith("VS Code", detection.Reason);
    }

    [Fact]
    public void Alacritty_reads_the_font_family_from_its_toml()
    {
        _env.Set("TERM", "alacritty");
        _fs.AddFile($"{Home}/.config/alacritty/alacritty.toml", new MockFileData("""
            [font.normal]
            family = "Hack Nerd Font Mono"
            """));

        Assert.True(Detect().NerdFont);
    }

    private FontDetection Detect(Func<string, string?>? plistToXml = null) =>
        TerminalFontDetection.Detect(_env, _fs, plistToXml);

    private static string? ITerm2Plist(string path) => """
        <?xml version="1.0" encoding="UTF-8"?>
        <plist version="1.0">
        <dict>
            <key>Default Bookmark Guid</key>
            <string>A</string>
            <key>New Bookmarks</key>
            <array>
                <dict>
                    <key>Guid</key><string>A</string>
                    <key>Name</key><string>Default</string>
                    <key>Normal Font</key><string>Menlo-Regular 12</string>
                    <key>Use Non-ASCII Font</key><false/>
                    <key>Non Ascii Font</key><string>HackNF-Regular 12</string>
                </dict>
                <dict>
                    <key>Guid</key><string>B</string>
                    <key>Name</key><string>Work</string>
                    <key>Normal Font</key><string>JetBrainsMonoNFM-Regular 13</string>
                </dict>
                <dict>
                    <key>Guid</key><string>C</string>
                    <key>Name</key><string>Split</string>
                    <key>Normal Font</key><string>Menlo-Regular 12</string>
                    <key>Use Non-ASCII Font</key><true/>
                    <key>Non Ascii Font</key><string>HackNF-Regular 12</string>
                </dict>
            </array>
        </dict>
        </plist>
        """;
}
