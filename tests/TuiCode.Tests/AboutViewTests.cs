using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using TuiCode.Workbench.About;

namespace TuiCode.Tests;

public class AboutViewTests
{
    [Fact]
    public void Ctor_shows_the_version_and_repository()
    {
        using var view = new AboutView("1.2.3");

        var details = view.SubViews.OfType<Label>().Single(label => label.Text.Contains("Version"));

        Assert.Contains("Version 1.2.3", details.Text);
        Assert.Contains(AboutView.RepositoryUrl, details.Text);
    }
}

public class AboutImageTests
{
    [Fact]
    public void Load_decodes_the_embedded_artwork()
    {
        var pixels = AboutImage.Load();

        Assert.Equal(640, pixels.GetLength(0));
        Assert.Equal(349, pixels.GetLength(1));
    }

    [Fact]
    public void Fit_fills_the_columns_and_ends_on_a_whole_row()
    {
        var (pixels, rows) = AboutImage.Fit(new System.Drawing.Size(640, 349), new System.Drawing.SizeF(11, 26), 58);

        Assert.Equal(638, pixels.Width);
        Assert.Equal(13, rows);
        Assert.Equal(13 * 26, pixels.Height);
    }

    [Fact]
    public void Fit_rounds_fractional_cells_to_whole_pixels()
    {
        var (pixels, rows) = AboutImage.Fit(new System.Drawing.Size(640, 349), new System.Drawing.SizeF(10.5f, 20.5f), 58);

        Assert.Equal(609, pixels.Width);
        Assert.Equal(16, rows);
        Assert.Equal(328, pixels.Height);
    }

    [Fact]
    public void Cover_trims_the_top_and_bottom_to_keep_the_aspect_ratio()
    {
        var source = new Color[4, 4];
        for (var x = 0; x < 4; x++)
        {
            source[x, 0] = new Color(255, 0, 0);
            source[x, 1] = new Color(0, 255, 0);
            source[x, 2] = new Color(0, 255, 0);
            source[x, 3] = new Color(255, 0, 0);
        }

        var result = AboutImage.Cover(source, new System.Drawing.Size(4, 2));

        Assert.Equal(new Color(0, 255, 0), result[0, 0]);
        Assert.Equal(new Color(0, 255, 0), result[3, 1]);
    }
}

public class SixelProbeTests
{
    [Fact]
    public void ParseIterm2CellSize_reads_width_and_height()
    {
        var size = SixelProbe.ParseIterm2CellSize("\x1b]1337;ReportCellSize=26.5;11.0;2.0\x1b\\");

        Assert.Equal(new System.Drawing.SizeF(11f, 26.5f), size);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\x1b[?64;1;2;4c")]
    [InlineData("\x1b]1337;ReportCellSize=0;11\x1b\\")]
    public void ParseIterm2CellSize_rejects_other_replies(string? response) =>
        Assert.Null(SixelProbe.ParseIterm2CellSize(response));

    [Fact]
    public void ParseCellResolution_reads_width_and_height() =>
        Assert.Equal(new System.Drawing.SizeF(10, 21), SixelProbe.ParseCellResolution("\x1b[6;21;10t"));

    [Fact]
    public void ParseCellResolution_rejects_other_replies() =>
        Assert.Null(SixelProbe.ParseCellResolution("\x1b[8;31;124t"));

    [Theory]
    [InlineData("\x1b[?64;1;2;4;6;17c", true)]
    [InlineData("\x1b[?62;4c", true)]
    [InlineData("\x1b[?62;22c", false)]
    [InlineData(null, false)]
    public void IndicatesSixel_looks_for_attribute_4(string? response, bool expected) =>
        Assert.Equal(expected, SixelProbe.IndicatesSixel(response));
}
