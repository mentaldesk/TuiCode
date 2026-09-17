using System.Diagnostics;
using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Tests;

// Timing-based, so Explicit: run with `dotnet test tests/TuiCode.Tests/TuiCode.Tests.csproj -c Release -- --explicit only`.
public class EditorEditBenchmarkTests : StaticConfigurationTest
{
    [Fact(Explicit = true)]
    public void Gutter_diff_and_cursor_moves_stay_well_under_a_frame_in_a_large_file()
    {
        var small = Measure(5_000);
        var large = Measure(50_000);

        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine("lines   | edit     | gutter diff | move cursor");
        foreach (var (lines, m) in new[] { (5_000, small), (50_000, large) })
            output.WriteLine($"{lines,7} | {m.Edit,5:F3} ms | {m.Diff,8:F3} ms | {m.MoveCursor,8:F3} ms");

        Assert.True(large.Diff < 2, $"gutter diff at 50,000 lines: {large.Diff:F3} ms");
        Assert.True(large.MoveCursor < 1, $"move cursor at 50,000 lines: {large.MoveCursor:F3} ms");
    }

    private static (double Edit, double Diff, double MoveCursor) Measure(int lines)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(200, 60);
        var fs = new MockFileSystem();
        fs.AddFile("/work/big.txt", new MockFileData(string.Join("\n", Enumerable.Repeat(new string('x', 120), lines))));
        using var tab = new EditorTab(fs.FileInfo.New("/work/big.txt")) { App = app, Width = 200, Height = 60 };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        _ = tab.LineChanges;

        const int warmup = 5;
        const int runs = 100;
        var edit = new Stopwatch();
        var diff = new Stopwatch();
        var move = new Stopwatch();
        for (var i = 0; i < warmup + runs; i++)
        {
            if (i == warmup)
            {
                edit.Reset();
                diff.Reset();
                move.Reset();
            }

            var row = lines / 2;
            edit.Start();
            tab.Replace(new TextMatch(row, 10, 1), i % 2 == 0 ? "y" : "x");
            edit.Stop();

            diff.Start();
            _ = tab.LineChanges;
            diff.Stop();

            move.Start();
            tab.MoveCursor(i % 2 == 0 ? lines - 2 : 1, 5);
            move.Stop();
        }
        return (edit.Elapsed.TotalMilliseconds / runs, diff.Elapsed.TotalMilliseconds / runs, move.Elapsed.TotalMilliseconds / runs);
    }
}
