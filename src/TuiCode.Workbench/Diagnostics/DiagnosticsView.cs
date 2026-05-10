using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Diagnostics;

public sealed class DiagnosticsView : Window
{
    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;
    private readonly Label _keyName;
    private readonly Label _keyHex;
    private readonly Label _keyBase;
    private readonly Label _keyRune;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Closed;

    public DiagnosticsView(string driverName)
    {
        Title = "Diagnostics";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 56;
        Height = 17;
        CanFocus = true;

        static Label Heading(string text, int y) => new Label
        {
            X = 1, Y = y,
            Text = text,
        };

        static Label Field(int y) => new Label
        {
            X = 13, Y = y,
            Width = Dim.Fill(1),
        };

        var driverHeading = Heading("Driver", 1);
        var driverNameLabel = new Label { X = 13, Y = 1, Text = driverName };

        var keyHeading = Heading("Last key", 4);
        var nameLabel = Heading("  Name", 5);
        var hexLabel = Heading("  Hex", 6);
        var baseLabel = Heading("  Base", 7);
        var runeLabel = Heading("  Rune", 8);

        _keyName = Field(5);
        _keyHex = Field(6);
        _keyBase = Field(7);
        _keyRune = Field(8);

        var hint = new Label
        {
            X = Pos.Center(),
            Y = 11,
            Text = "(press any key to update)",
        };

        var footer = new Label
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            Text = "Esc  close",
        };

        Add(driverHeading, driverNameLabel,
            keyHeading, nameLabel, hexLabel, baseLabel, runeLabel,
            _keyName, _keyHex, _keyBase, _keyRune,
            hint, footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
    }

    public void UpdateLastKey(Key key)
    {
        var raw = (uint)key.KeyCode;
        var modMask = (uint)(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask);
        var baseRaw = raw & ~modMask;
        var rune = key.AsRune.Value != 0
            ? $"U+{key.AsRune.Value:X4}  {key.AsGrapheme}"
            : "—";

        _keyName.Text = key.ToString();
        _keyHex.Text = $"0x{raw:X8}";
        _keyBase.Text = $"0x{baseRaw:X8}";
        _keyRune.Text = rune;
    }

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.DiagnosticsClose, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.DiagnosticsClose);
    }
}
