using System.Diagnostics;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Tests;

// Timing-based, so Explicit: run with `dotnet test tests/TuiCode.Tests/TuiCode.Tests.csproj -c Release -- --explicit only`.
public class EditorDrawBenchmarkTests : StaticConfigurationTest
{
    private const int Width = 200;
    private const int Height = 60;

    [Theory(Explicit = true)]
    [InlineData(5_000)]
    [InlineData(50_000)]
    public void Drawing_the_top_of_a_large_file_costs_about_the_same_as_drawing_its_end(int lines)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(Width, Height);
        var fs = new MockFileSystem();
        fs.AddFile("/work/big.txt", new MockFileData(string.Join("\n", Enumerable.Repeat(new string('x', 120), lines))));
        using var tab = new EditorTab(fs.FileInfo.New("/work/big.txt")) { App = app, Width = Width, Height = Height };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();

        var top = AverageDrawMs(app, tab);
        var edited = AverageDrawMs(app, tab, () => tab.Replace(new TextMatch(0, 0, 1), "x"));
        tab.MoveCursor(lines - 1, 0);
        var end = AverageDrawMs(app, tab);

        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{lines} lines, {Width}x{Height}: top {top:F1} ms, top after an edit {edited:F1} ms, end {end:F1} ms");
        Assert.Contains($"{lines}", app.Driver.ToString());
        Assert.True(top < end * 3 + 5, $"top {top:F1} ms vs end {end:F1} ms");
    }

    private static double AverageDrawMs(IApplication app, View view, Action? beforeEach = null)
    {
        const int warmup = 3;
        const int runs = 20;
        var sw = new Stopwatch();
        for (var i = 0; i < warmup + runs; i++)
        {
            if (i == warmup) sw.Reset();
            beforeEach?.Invoke();
            sw.Start();
            app.Driver!.ClearContents();
            app.Driver.Clip = new Region(app.Driver.Screen);
            view.SetNeedsDraw();
            view.Draw();
            sw.Stop();
        }
        return sw.Elapsed.TotalMilliseconds / runs;
    }
}
