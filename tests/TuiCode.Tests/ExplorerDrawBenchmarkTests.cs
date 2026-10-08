using System.Diagnostics;
using Terminal.Gui.Drawing;
using TuiCode.Explorer;

namespace TuiCode.Tests;

// Timing-based, so Explicit: run with `dotnet test tests/TuiCode.Tests/TuiCode.Tests.csproj -c Release -- --explicit only`.
public class ExplorerDrawBenchmarkTests : StaticConfigurationTest
{
    // $TMPDIR on macOS, where the a-team dashboard writes the log it hands to $VISUAL, holds tens of thousands (#450).
    [Theory(Explicit = true)]
    [InlineData(1_000)]
    [InlineData(20_000)]
    public void A_folder_of_thousands_of_folders_opens_and_draws_quickly(int folders)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(40, 30);
        var fs = new MockFileSystem();
        for (var i = 0; i < folders; i++)
            fs.AddFile($"/work/d{i}/x.cs", new MockFileData(""));
        using var explorer = new FileExplorerView { App = app, Width = 40, Height = 30 };
        explorer.BeginInit();
        explorer.EndInit();

        var open = Stopwatch.StartNew();
        explorer.Open(fs.DirectoryInfo.New("/work"));
        open.Stop();
        explorer.Layout();
        var draw = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            app.Driver.ClearContents();
            app.Driver.Clip = new Region(app.Driver.Screen);
            explorer.SetNeedsDraw();
            explorer.Draw();
        }
        draw.Stop();

        var perDraw = draw.Elapsed.TotalMilliseconds / 10;
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{folders} folders: open {open.Elapsed.TotalMilliseconds:F0} ms, draw {perDraw:F1} ms");
        Assert.True(perDraw < 50, $"draw {perDraw:F1} ms");
    }
}
