using TuiCode.Abstractions;
using TuiCode.Icons;

namespace TuiCode.Tests;

public class FileIconsTests
{
    [Theory]
    [InlineData("Program.cs", 0xf031b)]
    [InlineData("PROGRAM.CS", 0xf031b)]
    [InlineData("Dockerfile", 0xf0868)]
    [InlineData("app.spec.ts", 0xf499)]
    [InlineData("main.ts", 0xe628)]
    [InlineData(".bashrc", 0xe615)]
    public void Lookup_matches_file_names_then_the_longest_known_extension(string name, int codepoint)
    {
        var icon = FileIconTable.Lookup(name);

        Assert.Equal(char.ConvertFromUtf32(codepoint), icon?.Glyph);
    }

    [Fact]
    public void Lookup_prefers_a_compound_extension_over_its_last_part() =>
        Assert.Equal(0xd59855, FileIconTable.Lookup("types.d.ts")?.DarkColor);

    [Theory]
    [InlineData("notes")]
    [InlineData("archive.unknownext")]
    [InlineData("trailing.")]
    public void Lookup_finds_nothing_for_unknown_names(string name) =>
        Assert.Null(FileIconTable.Lookup(name));

    [Fact]
    public void The_generated_table_loads() => Assert.True(FileIconTable.Count > 500);

    [Fact]
    public void Nerd_font_icons_carry_a_colour_for_each_background()
    {
        var icons = new FileIcons(Detected(true));

        var icon = icons.ForFile("Program.cs")!.Value;

        Assert.Equal(0x596706, icon.ColorFor(darkBackground: true));
        Assert.Equal(0x434d04, icon.ColorFor(darkBackground: false));
    }

    [Fact]
    public void A_file_with_no_nerd_font_icon_gets_the_generic_one()
    {
        var icons = new FileIcons(Detected(true));

        Assert.Equal(icons.ForFile("notes"), icons.ForFile("other"));
        Assert.NotNull(icons.ForFile("notes"));
    }

    [Theory]
    [InlineData(true, FileIconStyle.NerdFont)]
    [InlineData(false, FileIconStyle.Emoji)]
    public void Auto_follows_the_detection(bool nerdFont, FileIconStyle expected)
    {
        var icons = new FileIcons(Detected(nerdFont));

        Assert.Equal(expected, icons.Style);
    }

    [Fact]
    public void An_explicit_setting_skips_detection()
    {
        var icons = new FileIcons(() => throw new InvalidOperationException("detected"))
        {
            Setting = FileIconStyle.Emoji,
        };

        Assert.Equal("📄", icons.ForFile("Program.cs")?.Glyph);
        Assert.Equal("📂", icons.ForDirectory(expanded: true)?.Glyph);
    }

    [Fact]
    public void Off_draws_no_icons()
    {
        var icons = new FileIcons(Detected(true)) { Setting = FileIconStyle.Off };

        Assert.Null(icons.ForFile("Program.cs"));
        Assert.Null(icons.ForDirectory(expanded: false));
        Assert.Null(icons.ForThreads(unresolved: true));
    }

    [Fact]
    public void A_file_with_review_threads_gets_a_chat_icon_per_style()
    {
        var nerd = new FileIcons(Detected(true));
        var emoji = new FileIcons(Detected(false));

        Assert.Equal("\U000F0B79", nerd.ForThreads(unresolved: true)?.Glyph);
        Assert.Equal("\U000F1414", nerd.ForThreads(unresolved: false)?.Glyph);
        Assert.Equal("💬", emoji.ForThreads(unresolved: true)?.Glyph);
        Assert.Equal("💭", emoji.ForThreads(unresolved: false)?.Glyph);
    }

    [Theory]
    [InlineData(GitChangeKind.Added, 0xeadc, "A", 0x81b88b, 0x587c0c)]
    [InlineData(GitChangeKind.Modified, 0xeade, "M", 0xe2c08d, 0x895503)]
    [InlineData(GitChangeKind.Deleted, 0xeadf, "D", 0xc74e39, 0xad0707)]
    [InlineData(GitChangeKind.Renamed, 0xeae0, "R", 0x73a5e6, 0x0451a5)]
    public void A_change_is_a_diff_icon_or_its_letter_in_the_same_colours(GitChangeKind kind, int codepoint, string letter, int dark, int light)
    {
        var nerd = new FileIcons(Detected(true)).ForChange(kind);
        var emoji = new FileIcons(Detected(false)).ForChange(kind);

        Assert.Equal(new FileIcon(char.ConvertFromUtf32(codepoint), dark, light), nerd);
        Assert.Equal(new FileIcon(letter, dark, light), emoji);
    }

    [Fact]
    public void With_icons_off_a_change_has_no_mark()
    {
        var icons = new FileIcons(Detected(true)) { Setting = FileIconStyle.Off };

        Assert.Null(icons.ForChange(GitChangeKind.Added));
    }

    [Theory]
    [InlineData(GitChangeKind.Added, "gitDecoration.addedResourceForeground")]
    [InlineData(GitChangeKind.Modified, "gitDecoration.modifiedResourceForeground")]
    [InlineData(GitChangeKind.Deleted, "gitDecoration.deletedResourceForeground")]
    [InlineData(GitChangeKind.Renamed, "gitDecoration.renamedResourceForeground")]
    public void A_themes_git_decoration_colour_overrides_the_default_on_both_backgrounds(GitChangeKind kind, string key)
    {
        var icons = new FileIcons(Detected(true));

        var mark = icons.ForChange(kind, new Dictionary<string, string> { [key] = "#123456" })!.Value;

        Assert.Equal(0x123456, mark.DarkColor);
        Assert.Equal(0x123456, mark.LightColor);
    }

    [Fact]
    public void A_theme_without_git_decoration_colours_gets_the_defaults()
    {
        var icons = new FileIcons(Detected(true));

        var themed = icons.ForChange(GitChangeKind.Added, new Dictionary<string, string> { ["editor.foreground"] = "#123456" });

        Assert.Equal(icons.ForChange(GitChangeKind.Added), themed);
    }

    [Fact]
    public void Changing_the_setting_raises_Changed_once()
    {
        var icons = new FileIcons(Detected(true));
        var changes = 0;
        icons.Changed += (_, _) => changes++;

        icons.Setting = FileIconStyle.Off;
        icons.Setting = FileIconStyle.Off;

        Assert.Equal(1, changes);
    }

    private static Func<FontDetection> Detected(bool nerdFont) => () => new FontDetection(nerdFont, "test");
}
