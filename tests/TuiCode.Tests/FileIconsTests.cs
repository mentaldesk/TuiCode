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
