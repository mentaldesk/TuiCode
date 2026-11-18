using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Editor;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// The keep words drawn after a conflict's <<<<<<< line (#693). Renders through a TG driver — serialised (#77).
public class EditorConflictWordsTests : StaticConfigurationTest
{
    private static readonly string[] Lines = ["above", "<<<<<<< HEAD", "mine", "=======", "theirs", ">>>>>>> main", "below"];

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public EditorConflictWordsTests() => _app.Driver!.SetScreenSize(100, 12);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Theory]
    [InlineData("Keep current", new[] { "above", "mine", "below" })]
    [InlineData("Keep incoming", new[] { "above", "theirs", "below" })]
    [InlineData("Keep both", new[] { "above", "mine", "theirs", "below" })]
    public void Clicking_a_keep_word_keeps_that_side(string word, string[] kept)
    {
        using var tab = Tab();
        var screen = Render(tab);
        Assert.Contains("Current change (HEAD) · Keep current · Keep incoming · Keep both", screen[1]);

        Click(tab, new Point(screen[1].IndexOf(word, StringComparison.Ordinal) + 2, 1));

        Assert.Equal(kept, tab.Lines);
        Assert.DoesNotContain("Keep", string.Concat(Render(tab)));
    }

    [Fact]
    public void Clicking_beside_the_words_leaves_the_conflict_alone()
    {
        using var tab = Tab();
        var screen = Render(tab);

        Click(tab, new Point(screen[1].IndexOf("Current change", StringComparison.Ordinal) + 2, 1));
        Click(tab, new Point(screen[1].IndexOf("Keep both", StringComparison.Ordinal) + "Keep both".Length + 1, 1));

        Assert.Equal(Lines, tab.Lines);
    }

    [Fact]
    public void One_undo_after_a_click_puts_the_block_back()
    {
        using var tab = Tab();
        var screen = Render(tab);

        Click(tab, new Point(screen[1].IndexOf("Keep both", StringComparison.Ordinal), 1));
        Assert.NotEqual(Lines, tab.Lines);
        tab.Code.Undo();

        Assert.Equal(Lines, tab.Lines);
    }

    private static void Click(EditorTab tab, Point at)
    {
        foreach (var flags in new[] { MouseFlags.LeftButtonPressed, MouseFlags.LeftButtonReleased, MouseFlags.LeftButtonClicked })
            tab.Code.NewMouseEvent(new Mouse { Flags = flags, Position = at });
    }

    private EditorTab Tab()
    {
        _fs.AddFile("/work/a.txt", new MockFileData(string.Join('\n', Lines)));
        var tab = new EditorTab(_fs.FileInfo.New("/work/a.txt")) { App = _app, Width = 100, Height = 12 };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        return tab;
    }

    // The rows of the text area alone, so a column is the one a click on it reports.
    private string[] Render(EditorTab tab)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        tab.Layout();
        tab.SetNeedsDraw();
        tab.Draw();
        var view = tab.Code;
        return Enumerable.Range(0, view.Viewport.Height)
            .Select(row => string.Concat(Enumerable.Range(0, view.Viewport.Width).Select(col =>
            {
                var screen = view.ViewportToScreen(new Point(col, row));
                return driver.Contents![screen.Y, screen.X].Grapheme;
            })))
            .ToArray();
    }
}
