using System.Diagnostics;
using Terminal.Gui.Views;
using TuiCode.Editor;

namespace TuiCode.Tests;

// Timing-based, so Explicit: run with `dotnet test tests/TuiCode.Tests/TuiCode.Tests.csproj -c Release -- --explicit only`.
public class EditorTypingBenchmarkTests : StaticConfigurationTest
{
    [Fact(Explicit = true)]
    public void Typing_on_an_ordinary_line_stays_well_under_a_frame_in_a_large_file()
    {
        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine("lines  | type      | backspace | type on widest line | enter, backspace");
        var typed = 0.0;
        foreach (var lines in new[] { 5_000, 50_000 })
        {
            var type = Measure(lines, onWidestLine: false, i => Key.Z);
            var backspace = Measure(lines, onWidestLine: false, i => i % 2 == 0 ? Key.Z : Key.Backspace);
            var widest = Measure(lines, onWidestLine: true, i => Key.Z);
            var enter = Measure(lines, onWidestLine: false, i => i % 2 == 0 ? Key.Enter : Key.Backspace);
            output.WriteLine($"{lines,6} | {type,6:F3} ms | {backspace,6:F3} ms | {widest,16:F3} ms | {enter,13:F3} ms");
            typed = Math.Max(type, backspace);
        }

        Assert.True(typed < 1, $"typing at 50,000 lines: {typed:F3} ms");
    }

    // Keystroke plus layout, mid-file. Line widths vary, with one line wider than the rest.
    private static double Measure(int lines, bool onWidestLine, Func<int, Key> key)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(200, 60);
        var widestRow = lines / 3;
        var text = Enumerable.Range(0, lines).Select(i => i == widestRow ? new string('w', 140) : new string('x', i * 37 % 100));
        var fs = new MockFileSystem();
        fs.AddFile("/work/big.txt", new MockFileData(string.Join("\n", text)));
        using var tab = new EditorTab(fs.FileInfo.New("/work/big.txt")) { App = app, Width = 200, Height = 60 };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        var view = tab.SubViews.OfType<TextView>().Single();
        var row = onWidestLine ? widestRow : lines / 2 + 1;
        view.InsertionPoint = new System.Drawing.Point(onWidestLine ? 140 : 10, row);
        tab.Layout();

        const int warmup = 5;
        const int runs = 40;
        var stopwatch = new Stopwatch();
        for (var i = 0; i < warmup + runs; i++)
        {
            if (i == warmup) stopwatch.Reset();
            stopwatch.Start();
            view.NewKeyDownEvent(key(i));
            tab.Layout();
            stopwatch.Stop();
        }
        return stopwatch.Elapsed.TotalMilliseconds / runs;
    }
}
