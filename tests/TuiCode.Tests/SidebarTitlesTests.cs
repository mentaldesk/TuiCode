using TuiCode.Abstractions;
using TuiCode.Workbench.Parts;

namespace TuiCode.Tests;

public class SidebarTitlesTests
{
    [Fact]
    public void With_icons_off_the_headers_are_the_names() =>
        Assert.Equal(["Explorer", "Find", "Usages", "Review", "AI assistance"], SidebarPart.HeaderTitles(FileIconStyle.Off, 4, 30));

    [Fact]
    public void With_emoji_the_selected_tab_shows_its_icon_and_name_and_the_others_their_icon() =>
        Assert.Equal(["📁", "🔍", "🔗", "👀", "✨ AI assistance"], SidebarPart.HeaderTitles(FileIconStyle.Emoji, 4, 40));

    [Fact]
    public void With_Nerd_Font_the_headers_are_the_pitchs_glyphs() =>
        Assert.Equal([" Explorer", "\U000F021E", "", "", ""], SidebarPart.HeaderTitles(FileIconStyle.NerdFont, 0, 30));

    [Fact]
    public void A_selected_name_too_long_for_the_room_left_is_cut() =>
        Assert.Equal(["📁", "🔍", "🔗", "👀", "✨ AI assista…"], SidebarPart.HeaderTitles(FileIconStyle.Emoji, 4, 28));

    [Theory]
    [InlineData(FileIconStyle.NerdFont)]
    [InlineData(FileIconStyle.Emoji)]
    public void At_the_narrowest_sidebar_every_header_still_fits(FileIconStyle style)
    {
        const int strip = SidebarSizing.Min - 2;
        for (var selected = 0; selected < 5; selected++)
        {
            var titles = SidebarPart.HeaderTitles(style, selected, strip);
            Assert.True(titles.Sum(t => Terminal.Gui.Text.StringExtensions.GetColumns(t) + 1) + 1 <= strip, string.Join("|", titles));
        }
    }

    [Fact]
    public void Emoji_too_wide_for_the_narrowest_sidebar_give_way_to_initials() =>
        Assert.Equal(["E", "F", "U", "R", "A"], SidebarPart.HeaderTitles(FileIconStyle.Emoji, 4, SidebarSizing.Min - 2));
}
