using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Workbench.Controls;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// Where typing goes in a dialog's box is the terminal's own cursor, as in the editor (#223).
public class InputViewCursorTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly View _host;
    private readonly InputView _input = new() { Width = Dim.Fill(), Height = Dim.Fill(1), Text = "Hi" };
    private readonly Button _elsewhere = new() { Y = Pos.AnchorEnd(1), Text = "Ok" };

    public InputViewCursorTests()
    {
        _app.Driver!.SetScreenSize(20, 6);
        _host = new View { App = _app, Width = 20, Height = 6, CanFocus = true };
        _host.Add(_input, _elsewhere);
    }

    public override void Dispose()
    {
        _host.Dispose();
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void The_cursor_sits_in_the_cell_the_next_character_will_go_into()
    {
        CaretAt(2);

        Assert.Equal(TextView.DefaultCursorStyle, _input.Cursor.Style);
        Assert.Equal(_input.ViewportToScreen(new Point(2, 0)), _input.Cursor.Position);
    }

    [Fact]
    public void Moving_the_caret_moves_it()
    {
        CaretAt(2);

        _input.NewKeyDownEvent(Key.CursorLeft);

        Assert.Equal(_input.ViewportToScreen(new Point(1, 0)), _input.Cursor.Position);
    }

    [Fact]
    public void The_character_the_cursor_is_on_stays_readable()
    {
        CaretAt(1);

        Assert.Equal("H", At(0, 0).Grapheme);
        Assert.Equal("i", At(1, 0).Grapheme);
    }

    /// <summary>Focuses the box with its caret at <paramref name="column"/>, drawn. The caret only holds once it's laid out.</summary>
    private void CaretAt(int column)
    {
        _input.SetFocus();
        Render();
        _input.InsertionPoint = new Point(column, 0);
        Render();
    }

    /// <summary>A cell of the box's text, in its own viewport coordinates.</summary>
    private Cell At(int x, int y)
    {
        var screen = _input.ViewportToScreen(new Point(x, y));
        return _app.Driver!.Contents![screen.Y, screen.X];
    }

    private void Render()
    {
        Initialize();
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        _host.SetNeedsDraw();
        _host.Draw();
    }

    private void Initialize()
    {
        if (!_host.IsInitialized)
        {
            _host.BeginInit();
            _host.EndInit();
        }
        _host.Layout();
    }
}
