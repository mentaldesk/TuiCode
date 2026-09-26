using System.Text;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench.Themes;

namespace TuiCode.Tests;

// Both places a changed file is named read the colour from the theme's Warning scheme (#268), and a
// deleted one is named in it too (#271).
public class DiskChangeColourTests : StaticConfigurationTest
{
    private const int Width = 30;

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();
    private readonly IFileInfo _file;

    public DiskChangeColourTests()
    {
        ConfigurationManager.Enable(ConfigLocations.None);
        ConfigurationManager.RuntimeConfig = BundledThemes.Config;
        ConfigurationManager.Load(ConfigLocations.LibraryResources | ConfigLocations.Runtime);
        ThemeManager.Theme = BundledThemes.Midnight;
        ConfigurationManager.Apply();
        _fs.AddFile("/work/a.txt", new MockFileData("one\n"));
        _file = _fs.FileInfo.New("/work/a.txt");
        _app.Driver!.SetScreenSize(Width, 6);
    }

    public override void Dispose()
    {
        _app.Dispose();
        ConfigurationManager.Disable(resetToHardCodedDefaults: true);
        base.Dispose();
    }

    [Theory]
    [InlineData(BundledThemes.Midnight)]
    [InlineData(BundledThemes.Daylight)]
    [InlineData(BundledThemes.TurboPascal)]
    [InlineData(BundledThemes.ModernBorland)]
    public void The_explorer_names_a_changed_file_in_the_warning_colour(string theme)
    {
        ThemeManager.Theme = theme;
        ConfigurationManager.Apply();
        using var explorer = new FileExplorerView { App = _app, Width = Width, Height = 4 };
        explorer.Open(_fs.DirectoryInfo.New("/work"));

        Render(explorer);
        var plain = Attribute(row: 1, col: 4);

        // From the tree's own node: MockFileSystem roots a POSIX path on Windows, and rows match on FullName.
        explorer.ShowChangedOnDisk([explorer.GetChildren(explorer.Root!).Single().FullName]);
        Render(explorer);
        var warned = Attribute(row: 1, col: 4);

        Assert.NotEqual(Warning(), plain.Foreground);
        Assert.Equal(Warning(), warned.Foreground);
        Assert.Equal(plain.Background, warned.Background);
    }

    [Fact]
    public void A_marked_tab_header_is_drawn_in_the_warning_colour()
    {
        using var group = new EditorGroup { App = _app, Width = Width, Height = 6 };
        var tab = group.OpenOrFocus(_file);

        Render(group);
        var plain = Attribute(row: 1, col: 1);

        tab.MarkOnDisk(DiskState.Changed);
        Render(group);
        var warned = Attribute(row: 1, col: 1);

        Assert.Equal("a.txt ⚠ ", tab.Title);
        Assert.NotEqual(Warning(), plain.Foreground);
        Assert.Equal(Warning(), warned.Foreground);
        Assert.Equal(plain.Background, warned.Background);
    }

    [Fact]
    public void A_deleted_file_is_named_in_the_warning_colour_too()
    {
        using var group = new EditorGroup { App = _app, Width = Width, Height = 6 };
        var tab = group.OpenOrFocus(_file);

        tab.MarkOnDisk(DiskState.Gone);
        Render(group);

        Assert.Equal("a.txt \u2298 ", tab.Title);
        Assert.Equal(Warning(), Attribute(row: 1, col: 1).Foreground);
        Assert.Contains("a.txt \u2298 \u2502", Row(1));
    }

    [Fact]
    public void Clearing_the_marker_puts_the_header_colour_back()
    {
        using var group = new EditorGroup { App = _app, Width = Width, Height = 6 };
        var tab = group.OpenOrFocus(_file);
        Render(group);
        var plain = Attribute(row: 1, col: 1);

        tab.MarkOnDisk(DiskState.Changed);
        Render(group);
        tab.MarkOnDisk(DiskState.Unchanged);
        Render(group);

        Assert.Equal(plain, Attribute(row: 1, col: 1));
    }

    [Fact]
    public void The_marker_is_drawn_clear_of_the_tab_header_border()
    {
        using var group = new EditorGroup { App = _app, Width = Width, Height = 6 };
        var tab = group.OpenOrFocus(_file);

        tab.MarkOnDisk(DiskState.Changed);
        Render(group);

        Assert.Contains("a.txt ⚠ │", Row(1));
    }

    private static Color Warning() => SchemeManager.GetScheme(WarningColour.SchemeName).Normal.Foreground;

    private void Render(View view)
    {
        if (!view.IsInitialized)
        {
            view.BeginInit();
            view.EndInit();
        }
        view.Layout();
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.SetNeedsDraw();
        view.Draw();
    }

    private Cell Cell(int row, int col) => _app.Driver!.Contents![row, col];

    private Terminal.Gui.Drawing.Attribute Attribute(int row, int col) => Cell(row, col).Attribute!.Value;

    private string Row(int row)
    {
        var sb = new StringBuilder();
        for (var col = 0; col < Width; col += Math.Max(1, Cell(row, col).Grapheme.GetColumns()))
            sb.Append(Cell(row, col).Grapheme);
        return sb.ToString();
    }
}
