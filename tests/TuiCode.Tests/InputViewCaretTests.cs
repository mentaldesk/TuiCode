using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Editor;
using TuiCode.Workbench.Controls;

namespace TuiCode.Tests;

// The caret the box paints for itself (#223), drawn through a real driver.
public class InputViewCaretTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly View _host;
    private readonly InputView _input = new() { Width = Dim.Fill(), Height = Dim.Fill(1), Text = "Hi" };
    private readonly Button _elsewhere = new() { Y = Pos.AnchorEnd(1), Text = "Ok" };

    public InputViewCaretTests()
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
    public void The_caret_is_painted_where_the_next_character_will_go()
    {
        CaretAt(2);

        Assert.Equal(CaretTextView.Bar, At(2, 0).Grapheme);
    }

    [Fact]
    public void The_caret_is_a_bar_down_the_cell_it_sits_before()
    {
        CaretAt(1);

        Assert.Equal(CaretTextView.Bar, At(1, 0).Grapheme);
        Assert.Equal("H", At(0, 0).Grapheme);
    }

    [Fact]
    public void An_empty_box_still_shows_where_typing_will_start()
    {
        _input.Text = string.Empty;

        CaretAt(0);

        Assert.Equal(CaretTextView.Bar, At(0, 0).Grapheme);
    }

    [Fact]
    public void Moving_the_caret_asks_for_the_repaint_that_moves_it()
    {
        CaretAt(2);
        Assert.False(_input.NeedsDraw);

        _input.NewKeyDownEvent(Key.CursorLeft);

        // Without this the caret stays where it was painted until the next edit forces a frame.
        Assert.True(_input.NeedsDraw);
        Render();
        Assert.Equal(CaretTextView.Bar, At(1, 0).Grapheme);
        Assert.Equal(" ", At(2, 0).Grapheme);
    }

    [Fact]
    public void The_terminal_cursor_is_hidden_while_the_box_paints_its_own()
    {
        CaretAt(2);

        Assert.Equal(CursorStyle.Hidden, _input.Cursor.Style);
    }

    [Fact]
    public void Focus_elsewhere_paints_no_caret_and_leaves_the_terminal_cursor_alone()
    {
        CaretAt(2);

        _elsewhere.SetFocus();
        Render();

        Assert.Equal(TextView.DefaultCursorStyle, _input.Cursor.Style);
        Assert.Equal(" ", At(2, 0).Grapheme);
    }

    /// <summary>Focuses the box with its caret at <paramref name="column"/>, drawn. The caret only holds once it's laid out.</summary>
    private void CaretAt(int column)
    {
        _input.SetFocus();
        Render();
        _input.InsertionPoint = new System.Drawing.Point(column, 0);
        Render();
    }

    /// <summary>A cell of the box's text, in its own viewport coordinates.</summary>
    private Cell At(int x, int y)
    {
        var screen = _input.ViewportToScreen(new System.Drawing.Point(x, y));
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
