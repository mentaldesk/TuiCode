using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Diagnostics;

public sealed class DiagnosticsView : Window
{
    private const int DialogWidth = 60;
    private const int FieldX = 13;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;
    private readonly Label _keyName;
    private readonly Label _keyHex;
    private readonly Label _keyBase;
    private readonly Label _keyRune;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Closed;

    public DiagnosticsView(string driverName, string kittyNegotiationStatus)
    {
        Title = "Diagnostics";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        CanFocus = true;

        var kittyStatusLines = WrapText(kittyNegotiationStatus, GetFieldWidth());
        var kittyStatusText = string.Join("\n", kittyStatusLines);
        var keyHeadingY = 2 + kittyStatusLines.Length + 1;
        var nameY = keyHeadingY + 1;
        var hintY = nameY + 6;
        Height = hintY + 6;

        static Label Heading(string text, int y) => new Label
        {
            X = 1, Y = y,
            Text = text,
        };

        static Label Field(int y) => new Label
        {
            X = FieldX, Y = y,
            Width = Dim.Fill(1),
        };

        var driverHeading = Heading("Driver", 1);
        var driverNameLabel = new Label { X = FieldX, Y = 1, Text = driverName };
        var kittyHeading = Heading("Kitty kb", 2);
        var kittyStatusLabel = new Label
        {
            X = FieldX,
            Y = 2,
            Width = GetFieldWidth(),
            Height = kittyStatusLines.Length,
            Text = kittyStatusText,
        };

        var keyHeading = Heading("Last key", keyHeadingY);
        var nameLabel = Heading("  Name", nameY);
        var hexLabel = Heading("  Hex", nameY + 1);
        var baseLabel = Heading("  Base", nameY + 2);
        var runeLabel = Heading("  Rune", nameY + 3);

        _keyName = Field(nameY);
        _keyHex = Field(nameY + 1);
        _keyBase = Field(nameY + 2);
        _keyRune = Field(nameY + 3);

        var hint = new Label
        {
            X = Pos.Center(),
            Y = hintY,
            Text = "(press any key to update)",
        };

        var footer = new Label
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            Text = "Esc  close",
        };

        Add(driverHeading, driverNameLabel, kittyHeading, kittyStatusLabel,
            keyHeading, nameLabel, hexLabel, baseLabel, runeLabel,
            _keyName, _keyHex, _keyBase, _keyRune,
            hint, footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
    }

    private static int GetFieldWidth() => DialogWidth - FieldX - 2;

    private static string[] WrapText(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
            return [string.Empty];

        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var remaining = paragraph.Trim();
            while (remaining.Length > width)
            {
                var breakAt = remaining.LastIndexOf(' ', width);
                if (breakAt > 0)
                {
                    var nextSpace = remaining.IndexOf(' ', breakAt + 1);
                    var runEnd = nextSpace < 0 ? remaining.Length : nextSpace;
                    if (runEnd - breakAt - 1 > width)
                        breakAt = width;
                }
                if (breakAt <= 0)
                    breakAt = width;

                lines.Add(remaining[..breakAt].TrimEnd());
                remaining = remaining[breakAt..].TrimStart();
            }

            lines.Add(remaining);
        }

        return [.. lines];
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
