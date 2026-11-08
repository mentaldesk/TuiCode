using MentalDesk.Tui.Fields;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Workbench.Controls;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// Every text box paints its own caret, so it shows in any terminal (#668).
public class TextBoxCaretTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly View _host;
    private readonly PlaceholderField _line = new() { Width = Dim.Fill(), Text = "Hi" };
    private readonly MultiLineField _box = new MultiLineField { Y = 1, Width = Dim.Fill(), Height = 4, Text = "Hi" }.HeavyWhileFocused();
    private readonly Button _elsewhere = new() { Y = Pos.AnchorEnd(1), Text = "Ok" };

    public TextBoxCaretTests()
    {
        _app.Driver!.SetScreenSize(20, 7);
        _host = new View { App = _app, Width = 20, Height = 7, CanFocus = true };
        _host.Add(_line, _box, _elsewhere);
    }

    public override void Dispose()
    {
        _host.Dispose();
        _app.Dispose();
        base.Dispose();
    }

    [Theory]
    [InlineData(0, "H")]
    [InlineData(1, "i")]
    [InlineData(2, " ")]
    public void A_single_line_box_paints_a_block_on_the_character_typing_goes_before(int column, string under)
    {
        Focus(_line);
        _line.InsertionPoint = column;
        Render();

        Assert.Equal(CursorStyle.Hidden, _line.Cursor.Style);
        Assert.Equal(PaintedCaretColours.Of(_line), At(_line, column, 0).Attribute);
        Assert.Equal(under, At(_line, column, 0).Grapheme);
    }

    [Theory]
    [InlineData(0, "H")]
    [InlineData(1, "i")]
    [InlineData(2, " ")]
    public void A_multi_line_box_paints_a_block_on_the_character_typing_goes_before(int offset, string under)
    {
        Focus(_box);
        _box.CaretOffset = offset;
        Render();

        Assert.Equal(CursorStyle.Hidden, _box.Cursor.Style);
        Assert.Equal(PaintedCaretColours.Of(_box), At(_box, offset, 0).Attribute);
        Assert.Equal(under, At(_box, offset, 0).Grapheme);
    }

    [Fact]
    public void Only_the_box_with_the_keys_paints_a_caret()
    {
        Focus(_box);
        _line.InsertionPoint = 0;
        Render();

        Assert.NotEqual(PaintedCaretColours.Of(_line), At(_line, 0, 0).Attribute);
        Assert.Equal(CursorStyle.Hidden, _app.Driver!.GetCursor().Style);
    }

    [Fact]
    public void An_arrow_moves_the_caret_at_once()
    {
        Focus(_box);
        _box.CaretOffset = 2;
        Render();

        _box.NewKeyDownEvent(Key.CursorLeft);
        Render();

        Assert.Equal(PaintedCaretColours.Of(_box), At(_box, 1, 0).Attribute);
        Assert.NotEqual(PaintedCaretColours.Of(_box), At(_box, 2, 0).Attribute);
    }

    [Fact]
    public void The_multi_line_box_is_heavy_only_while_it_has_the_keys()
    {
        Assert.Equal(LineStyle.Single, _box.BorderStyle);

        Focus(_box);
        Assert.Equal(LineStyle.Heavy, _box.BorderStyle);

        _elsewhere.SetFocus();
        Assert.Equal(LineStyle.Single, _box.BorderStyle);
    }

    private void Focus(View field)
    {
        Initialize();
        field.SetFocus();
        Render();
    }

    private Cell At(View field, int x, int y)
    {
        var screen = field.ViewportToScreen(new Point(x, y));
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
