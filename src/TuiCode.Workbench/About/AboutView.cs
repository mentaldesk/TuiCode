using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.About;

public sealed class AboutView : Window
{
    internal const string RepositoryUrl = "https://github.com/mentaldesk/TuiCode";

    internal static readonly string[] Art =
    [
        "                 _.--._",
        "               ,'  o   `==-     ████████╗ ██╗   ██╗ ██╗",
        "              /   ,--.          ╚══██╔══╝ ██║   ██║ ██║",
        "             /   ( @@ )            ██║    ██║   ██║ ██║",
        "            /  ,' `--'             ██║    ██║   ██║ ██║",
        "          ,'  /  / |               ██║    ╚██████╔╝ ██║",
        "        ,'  ,'  /  |               ╚═╝     ╚═════╝  ╚═╝",
        "      ,'  ,'  ,'  /",
        "    ,'  ,'  ,'  ,'                 c o d e : : e d i t o r",
        "  ,'  ,'__,'__,'",
        " /__,'   _||_",
        "═════════╪══╪══════════",
    ];

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Closed;

    public AboutView(string version)
    {
        Title = "About";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = Art.Max(line => line.Length) + 4;
        Height = Art.Length + 8;
        CanFocus = true;

        var art = new AboutArt
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Art.Length,
        };

        var details = new Label
        {
            X = Pos.Center(),
            Y = Pos.Bottom(art) + 1,
            Text = $"Version {version}\n{RepositoryUrl}",
            TextAlignment = Alignment.Center,
        };

        var footer = new Label
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            Text = "Esc · Enter  close",
        };

        Add(art, details, footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.AboutClose, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.AboutClose);
        _scopeKeybindings.Bind("Enter", CommandIds.AboutClose);
    }

    private sealed class AboutArt : View
    {
        private static readonly Color Green = new(0x2E, 0xA0, 0x43);

        protected override bool OnDrawingContent(DrawContext? context)
        {
            SetAttribute(GetAttributeForRole(VisualRole.Normal) with { Foreground = Green });
            for (var row = 0; row < Art.Length && row < Viewport.Height; row++)
            {
                Move(0, row);
                AddStr(Art[row]);
            }
            return true;
        }
    }
}
