using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Icons;
using TuiCode.Syntax;
using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class FileIconDrawingTests : StaticConfigurationTest
{
    private const string Folder = "\uf07c";
    private const string CSharp = "\U000f031b";
    private const string SymbolClass = "\ueb5b";
    private const string SymbolMethod = "\uea8c";

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly FileIcons _icons = new(() => new FontDetection(true, "test"));

    public FileIconDrawingTests() => _app.Driver!.SetScreenSize(20, 4);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void Explorer_rows_show_the_icon_before_the_name()
    {
        var explorer = Explorer();

        Render(explorer);

        Assert.Equal($"└-{Folder} work", Row(0).TrimEnd());
        Assert.Equal($"  └─{CSharp} Program.cs", Row(1).TrimEnd());
    }

    [Fact]
    public void The_icon_takes_its_colour_and_the_name_keeps_the_rows()
    {
        var explorer = Explorer();

        Render(explorer);

        var icon = Cell(1, 4).Attribute!.Value;
        var name = Cell(1, 6).Attribute!.Value;
        Assert.Equal(name.Background, icon.Background);
        Assert.NotEqual(name.Foreground, icon.Foreground);
    }

    [Fact]
    public void A_long_name_is_cut_at_the_edge_of_the_view()
    {
        var explorer = Explorer("AVeryLongFileNameIndeed.cs");

        Render(explorer);

        Assert.Equal($"  └─{CSharp} AVeryLongFileN", Row(1));
    }

    [Fact]
    public void Turning_icons_off_redraws_plain_names()
    {
        var explorer = Explorer();
        Render(explorer);

        _icons.Setting = FileIconStyle.Off;
        Render(explorer);

        Assert.Equal("  └─Program.cs", Row(1).TrimEnd());
    }

    [Fact]
    public void A_cut_item_is_drawn_dimmed_with_its_icon()
    {
        var explorer = Explorer();
        explorer.Cut(explorer.GetChildren(explorer.Root!).Single());

        Render(explorer);

        Assert.True(Cell(1, 4).Attribute!.Value.Style.HasFlag(TextStyle.Faint), "icon");
        Assert.True(Cell(1, 6).Attribute!.Value.Style.HasFlag(TextStyle.Faint), "name");
        Assert.False(Cell(0, 4).Attribute!.Value.Style.HasFlag(TextStyle.Faint), "the root isn't cut");
    }

    [Fact]
    public void Clearing_the_cut_redraws_the_item_normally()
    {
        var explorer = Explorer();
        explorer.Cut(explorer.GetChildren(explorer.Root!).Single());
        Render(explorer);

        explorer.ClearCut();
        Render(explorer);

        Assert.False(Cell(1, 6).Attribute!.Value.Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public void Wide_emoji_icons_draw_in_two_cells()
    {
        _icons.Setting = FileIconStyle.Emoji;
        var list = new ListView
        {
            App = _app,
            Width = 20,
            Height = 2,
            Source = new IconListSource(["src/", "notes"], [_icons.ForDirectory(false), _icons.ForFile("notes")]),
        };

        Render(list);

        Assert.Equal("📁 src/", Row(0).TrimEnd());
        Assert.Equal("📄 notes", Row(1).TrimEnd());
    }

    [Fact]
    public void Outline_rows_show_the_kind_icon_after_the_indent_and_ahead_of_the_name()
    {
        IReadOnlyList<FileSymbol> outline = [new("Widget", SymbolKind.Class, 0, 0), new("Go", SymbolKind.Method, 2, 1)];
        var list = new ListView
        {
            App = _app,
            Width = 20,
            Height = 2,
            Source = new SymbolListSource(outline, indent: true, FileIconStyle.NerdFont),
        };

        Render(list);

        Assert.Equal($"{SymbolClass} Widget    class  1", Row(0));
        Assert.Equal($"  {SymbolMethod} Go     method  3", Row(1));
    }

    [Fact]
    public void Outline_rows_with_icons_off_start_at_the_name()
    {
        IReadOnlyList<FileSymbol> outline = [new("Widget", SymbolKind.Class, 0, 0), new("Go", SymbolKind.Method, 2, 1)];
        var list = new ListView
        {
            App = _app,
            Width = 20,
            Height = 2,
            Source = new SymbolListSource(outline, indent: true, FileIconStyle.Off),
        };

        Render(list);

        Assert.Equal("Widget      class  1", Row(0));
    }

    private FileExplorerView Explorer(string file = "Program.cs")
    {
        var fs = new MockFileSystem();
        fs.AddFile($"/work/{file}", new MockFileData(""));
        var explorer = new FileExplorerView(_icons) { App = _app, Width = 20, Height = 4 };
        explorer.Open(fs.DirectoryInfo.New("/work"));
        return explorer;
    }

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

    private string Row(int row)
    {
        var sb = new StringBuilder();
        for (var col = 0; col < 20; col += Math.Max(1, Cell(row, col).Grapheme.GetColumns()))
            sb.Append(Cell(row, col).Grapheme);
        return sb.ToString();
    }
}
