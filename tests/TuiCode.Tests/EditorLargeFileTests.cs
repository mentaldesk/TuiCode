using System.Diagnostics;
using Terminal.Gui.Drawing;
using TuiCode.Editor;

namespace TuiCode.Tests;

// Against a small file on the same machine, so a slow runner slows both (#517).
public class EditorLargeFileTests : StaticConfigurationTest
{
    [Fact]
    public void Typing_and_scrolling_cost_about_the_same_in_a_50000_line_file_as_in_a_500_line_one()
    {
        var (smallTyping, smallScrolling) = Measure(500);
        var (typing, scrolling) = Measure(50_000);

        Assert.True(typing < smallTyping * 3 + 10, $"typing: {typing:F2} ms a key at 50,000 lines, {smallTyping:F2} ms at 500");
        Assert.True(scrolling < smallScrolling * 3 + 10, $"scrolling: {scrolling:F2} ms a page at 50,000 lines, {smallScrolling:F2} ms at 500");
    }

    private static (double Typing, double Scrolling) Measure(int lines)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(120, 40);
        var fs = new MockFileSystem();
        var text = Enumerable.Range(0, lines).Select(i => $"    var line{i} = Compute({i}, \"{new string('x', i * 37 % 60)}\");");
        fs.AddFile("/work/big.txt", new MockFileData(string.Join("\n", text)));
        using var tab = new EditorTab(fs.FileInfo.New("/work/big.txt")) { App = app, Width = 120, Height = 40 };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        tab.MoveCursor(lines / 2, 10);
        Frame(app, tab);

        var typing = Average(app, tab, i => tab.Code.NewKeyDownEvent(i % 2 == 0 ? Key.Z : Key.Backspace));
        var scrolling = Average(app, tab, i => tab.Code.NewKeyDownEvent(i % 2 == 0 ? Key.PageDown : Key.PageUp));
        return (typing, scrolling);
    }

    private static double Average(IApplication app, EditorTab tab, Action<int> key)
    {
        const int warmup = 4;
        const int runs = 20;
        var stopwatch = new Stopwatch();
        for (var i = 0; i < warmup + runs; i++)
        {
            if (i == warmup) stopwatch.Reset();
            stopwatch.Start();
            key(i);
            Frame(app, tab);
            stopwatch.Stop();
        }
        return stopwatch.Elapsed.TotalMilliseconds / runs;
    }

    private static void Frame(IApplication app, EditorTab tab)
    {
        app.Driver!.Clip = new Region(app.Driver.Screen);
        tab.Layout();
        tab.SetNeedsDraw();
        tab.Draw();
    }
}
