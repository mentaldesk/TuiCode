using System.Diagnostics;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Syntax;

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

    [Theory(Explicit = true)]
    [InlineData(5_000)]
    [InlineData(50_000)]
    public void Syntax_colouring_keeps_drawing_a_large_file_fast(int lines)
    {
        const string block = """
            /// <summary>A documented member.</summary>
            public async Task<int> ComputeAsync(string name, CancellationToken ct = default)
            {
                var total = 0; // running total
                foreach (var c in name) total += c * 31;
                return await Task.FromResult(total + $"{name}:{total}".Length);
            }
            """;
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(Width, Height);
        var fs = new MockFileSystem();
        var blocks = Enumerable.Repeat(block, lines / 7);
        fs.AddFile("/work/Big.cs", new MockFileData("class Big\n{\n" + string.Join("\n", blocks) + "\n}\n"));
        using var tab = new EditorTab(fs.FileInfo.New("/work/Big.cs"), new SyntaxHighlighter(GrammarBundle.Load()))
            { App = app, Width = Width, Height = Height };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        var text = tab.SubViews.OfType<EditorTextView>().Single();

        var first = Stopwatch.StartNew();
        Draw(app, tab);
        first.Stop();
        var top = AverageDrawMs(app, tab);
        var edited = AverageDrawMs(app, tab, () => tab.Replace(new TextMatch(2, 4, 1), "/"));

        tab.MoveCursor(text.Lines - 1, 0);
        var frames = 0;
        var catchUp = Stopwatch.StartNew();
        do
        {
            Draw(app, tab);
            frames++;
        } while (text.Syntax!.TokensFor(text.Lines - 1) is null);
        catchUp.Stop();
        var end = AverageDrawMs(app, tab);

        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{text.Lines} lines, {Width}x{Height}: first {first.Elapsed.TotalMilliseconds:F1} ms, top {top:F1} ms, " +
            $"top after an edit {edited:F1} ms, lexing to the end {catchUp.Elapsed.TotalMilliseconds:F0} ms over {frames} frames, end {end:F1} ms");
        Assert.True(edited < 50, $"top after an edit {edited:F1} ms");
    }

    private static void Draw(IApplication app, View view)
    {
        app.Driver!.ClearContents();
        app.Driver.Clip = new Region(app.Driver.Screen);
        view.SetNeedsDraw();
        view.Draw();
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
