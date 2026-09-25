using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Search;

namespace TuiCode.Tests;

// The Find pane's input rows, through a TG driver — serialised (#77).
public class SearchViewDrawingTests : StaticConfigurationTest
{
    private const int Columns = SidebarSizing.Min;

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly SearchView _view = new() { Width = Dim.Fill(), Height = Dim.Fill() };
    private readonly View _host;

    public SearchViewDrawingTests()
    {
        _app.Driver!.SetScreenSize(Columns, 6);
        _host = new View { App = _app, Width = Columns, Height = 6, CanFocus = true };
        _host.Add(_view);
    }

    public override void Dispose()
    {
        _host.Dispose();
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void Both_inputs_are_labelled_in_full_with_their_field_clear_of_the_label()
    {
        _view.ShowReplace(true);
        _view.Query = "q";
        _view.Replacement = "r";

        var rows = Render();

        Assert.Equal("Find    q", rows[0].TrimEnd());
        Assert.Equal("Replace r", rows[1].TrimEnd());
    }

    private string[] Render()
    {
        _host.BeginInit();
        _host.EndInit();
        _host.Layout();

        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        _host.SetNeedsDraw();
        _host.Draw();

        return Enumerable.Range(0, _host.Frame.Height).Select(row =>
        {
            var line = new StringBuilder();
            for (var col = 0; col < Columns; col++) line.Append(driver.Contents![row, col].Grapheme);
            return line.ToString();
        }).ToArray();
    }
}
